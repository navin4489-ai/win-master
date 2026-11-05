import { config } from "./config.js";
import { logger } from "./logger.js";
import { pushEvent } from "./callback.js";

// WhatsApp Web automation engine using whatsapp-web.js. Unlike Baileys (which speaks the
// WhatsApp protocol directly), this drives a real headless Chromium logged in to WhatsApp
// Web — i.e. it behaves like a person using the web client. It is heavier (a Chromium per
// account) but some users prefer it. Session auth is persisted to disk (LocalAuth) under
// SESSIONS_DIR so a linked account survives restarts.
//
// whatsapp-web.js + puppeteer are imported lazily so the bridge still boots if they aren't
// installed (e.g. a slim image without Chromium).

const sessions = new Map(); // accountId -> { client, status, qr }

let _wweb = null;
let _qrcode = null;
async function deps() {
  if (!_wweb) {
    _wweb = await import("whatsapp-web.js");
    _qrcode = (await import("qrcode")).default;
  }
  return { wweb: _wweb.default ?? _wweb, QRCode: _qrcode };
}

function authDir(accountId) {
  return `${config.sessionsDir}/wweb-${accountId}`;
}

export async function startSession(accountId, { reset = false } = {}) {
  const existing = sessions.get(accountId);
  if (existing?.status === "connected") return { status: "connected" };
  if (existing?.status === "qr" && existing.qr && existing.qr.expiresAt > Date.now()) return { status: "qr" };
  if (existing?.status === "connecting" && existing.startedAt && Date.now() - existing.startedAt < 90_000) {
    return { status: existing.status };
  }
  if (existing) {
    try { await existing.client?.destroy?.(); } catch { /* ignore */ }
    sessions.delete(accountId);
  }

  const { wweb, QRCode } = await deps();
  const { Client, LocalAuth } = wweb;

  const client = new Client({
    authStrategy: new LocalAuth({ clientId: `wm-${accountId}`, dataPath: config.sessionsDir }),
    puppeteer: {
      headless: true,
      executablePath: process.env.PUPPETEER_EXECUTABLE_PATH || undefined,
      // Flags required to run Chromium inside a container.
      args: [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--no-first-run",
        "--no-zygote",
        "--single-process",
      ],
    },
  });

  const entry = { client, status: "connecting", qr: null, startedAt: Date.now() };
  sessions.set(accountId, entry);

  client.on("qr", async (qr) => {
    try {
      const dataUrl = await QRCode.toDataURL(qr, { margin: 1, width: 320 });
      const qrPngBase64 = dataUrl.replace(/^data:image\/png;base64,/, "");
      entry.status = "qr";
      entry.qr = { qrData: qr, qrPngBase64, expiresAt: Date.now() + (config.qrTtlSeconds * 1000) };
      await pushEvent("qr", accountId, { qrData: qr, qrPngBase64 });
      logger.info({ accountId }, "wweb qr generated");
    } catch (err) {
      logger.error({ accountId, err: err.message }, "wweb failed to render qr");
    }
  });

  client.on("ready", async () => {
    entry.status = "connected";
    entry.qr = null;
    const phoneNumber = client.info?.wid?.user ?? null;
    await pushEvent("connected", accountId, { phoneNumber });
    logger.info({ accountId, phoneNumber }, "wweb session connected");
  });

  client.on("authenticated", () => logger.info({ accountId }, "wweb authenticated"));

  client.on("auth_failure", async (msg) => {
    entry.status = "disconnected";
    await pushEvent("disconnected", accountId, { reason: "auth_failure" });
    logger.warn({ accountId, msg }, "wweb auth failure");
  });

  client.on("disconnected", async (reason) => {
    entry.status = "disconnected";
    sessions.delete(accountId);
    // A logout/ban-like reason surfaces as account_restricted; everything else as a plain drop.
    if (String(reason).toLowerCase().includes("logout") || String(reason).toLowerCase().includes("ban")) {
      await pushEvent("account_restricted", accountId, { reason: String(reason) });
    } else {
      await pushEvent("disconnected", accountId, { reason: String(reason) });
    }
    logger.warn({ accountId, reason }, "wweb disconnected");
  });

  // Forward incoming 1:1 text to the API inbox/bot (mirrors the Baileys engine).
  client.on("message", async (m) => {
    try {
      if (m.fromMe) return;
      const from = (m.from || "").split("@")[0];
      if (!from || (m.from || "").endsWith("@g.us") || m.from === "status@broadcast") return;
      const body = m.body || "";
      if (!body) return;
      await pushEvent("message", accountId, { from, body, waMessageId: m.id?._serialized, timestamp: m.timestamp });
    } catch { /* ignore */ }
  });

  try {
    await client.initialize();
  } catch (err) {
    sessions.delete(accountId);
    logger.error({ accountId, err: err.message }, "wweb initialize failed");
    throw err;
  }
  return { status: "connecting" };
}

export function sessionStatus(accountId) {
  return sessions.get(accountId)?.status ?? "disconnected";
}

export function getQr(accountId) {
  const entry = sessions.get(accountId);
  if (!entry?.qr) return null;
  if (entry.qr.expiresAt && entry.qr.expiresAt < Date.now()) return null;
  return entry.qr;
}

export async function stopSession(accountId, { wipe = false } = {}) {
  const entry = sessions.get(accountId);
  if (entry?.client) {
    try { if (wipe) await entry.client.logout(); } catch { /* ignore */ }
    try { await entry.client.destroy(); } catch { /* ignore */ }
  }
  sessions.delete(accountId);
}

export async function sendMessage(accountId, { to, text, mediaUrl, mediaType }) {
  const entry = sessions.get(accountId);
  if (!entry?.client || entry.status !== "connected") throw new Error("session not connected");

  let digits = String(to).replace(/[^0-9]/g, "");
  if (digits.length === 11 && digits.startsWith("0")) digits = digits.slice(1);
  if (digits.length === 10 && "6789".includes(digits[0])) digits = "91" + digits;
  const chatId = `${digits}@c.us`;

  // Confirm the number is on WhatsApp so a bad number fails with a clear reason.
  try {
    const numberId = await entry.client.getNumberId(digits);
    if (!numberId) throw new Error("recipient is not on WhatsApp");
  } catch (err) {
    if (err.message === "recipient is not on WhatsApp") throw err;
    // If the check itself fails, fall through and attempt the send.
  }

  const { wweb } = await deps();
  if (mediaUrl && mediaType && mediaType !== "None") {
    const { MessageMedia } = wweb;
    const media = await MessageMedia.fromUrl(mediaUrl, { unsafeMime: true });
    const sent = await entry.client.sendMessage(chatId, media, { caption: text || undefined });
    return sent?.id?._serialized ?? null;
  }
  const sent = await entry.client.sendMessage(chatId, text || "");
  return sent?.id?._serialized ?? null;
}

export function sessionCount() {
  return sessions.size;
}

// Restore disk-persisted WhatsApp Web sessions on boot (LocalAuth keeps the auth on disk,
// so re-initialising a client with the same clientId reconnects without a re-scan).
export async function restoreSessions(accountIds) {
  for (const id of accountIds || []) {
    startSession(id).catch((e) => logger.warn({ id, err: e.message }, "wweb restore failed"));
  }
}
