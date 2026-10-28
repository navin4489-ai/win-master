import { mkdirSync, rmSync } from "node:fs";
import { join } from "node:path";
import { config } from "./config.js";
import { logger } from "./logger.js";
import { pushEvent } from "./callback.js";

// In-memory registry of live sessions keyed by accountId.
// Each entry: { sock, status, connecting, qr }.
const sessions = new Map();

// Baileys is imported lazily so the bridge still boots and serves /healthz
// even on a machine where the native-ish package failed to install.
let _baileys = null;
let _qrcode = null;
async function deps() {
  if (!_baileys) {
    _baileys = await import("@whiskeysockets/baileys");
    _qrcode = (await import("qrcode")).default;
  }
  return { b: _baileys, QRCode: _qrcode };
}

function sessionDir(accountId) {
  return join(config.sessionsDir, String(accountId));
}

export function normalizeJid(phone) {
  const digits = String(phone).replace(/[^0-9]/g, "");
  return `${digits}@s.whatsapp.net`;
}

// Starts (or restarts) a Baileys socket for an account and wires its lifecycle.
// Safe to call repeatedly; a connection already in progress is reused.
export async function startSession(accountId) {
  const existing = sessions.get(accountId);
  if (existing?.connecting || existing?.status === "connected") {
    return { status: existing.status };
  }

  const { b, QRCode } = await deps();
  const { makeWASocket, useMultiFileAuthState, fetchLatestBaileysVersion, DisconnectReason, Browsers } = b;
  const { Boom } = await import("@hapi/boom");

  mkdirSync(sessionDir(accountId), { recursive: true });
  const { state, saveCreds } = await useMultiFileAuthState(sessionDir(accountId));
  const { version } = await fetchLatestBaileysVersion();

  const sock = makeWASocket({
    version,
    auth: state,
    printQRInTerminal: false,
    browser: Browsers.appropriate("Chrome"),
    markOnlineOnConnect: false,
    syncFullHistory: false,
  });

  sessions.set(accountId, { sock, status: "connecting", connecting: true, qr: null });
  sock.ev.on("creds.update", saveCreds);

  sock.ev.on("connection.update", async (update) => {
    const { connection, lastDisconnect, qr } = update;

    if (qr) {
      try {
        const qrPngDataUrl = await QRCode.toDataURL(qr, { margin: 1, width: 320 });
        const qrPngBase64 = qrPngDataUrl.replace(/^data:image\/png;base64,/, "");
        const entry = sessions.get(accountId);
        if (entry) {
          entry.status = "qr";
          entry.qr = { qrData: qr, qrPngBase64, expiresAt: Date.now() + config.qrTtlSeconds * 1000 };
        }
        await pushEvent("qr", accountId, { qrData: qr, qrPngBase64 });
        logger.info({ accountId }, "qr generated");
      } catch (err) {
        logger.error({ accountId, err: err.message }, "failed to render qr");
      }
    }

    if (connection === "open") {
      const entry = sessions.get(accountId);
      if (entry) { entry.status = "connected"; entry.connecting = false; entry.qr = null; }
      const phoneNumber = sock.user?.id?.split(":")[0] ?? null;
      await pushEvent("connected", accountId, { phoneNumber });
      logger.info({ accountId, phoneNumber }, "session connected");
    }

    if (connection === "close") {
      const code = new Boom(lastDisconnect?.error)?.output?.statusCode;
      const loggedOut = code === DisconnectReason.loggedOut;
      sessions.delete(accountId);
      if (loggedOut) {
        rmSync(sessionDir(accountId), { recursive: true, force: true });
        await pushEvent("disconnected", accountId, { reason: "logged_out" });
        logger.warn({ accountId }, "logged out, credentials cleared");
      } else {
        logger.warn({ accountId, code }, "connection closed, reconnecting");
        setTimeout(() => startSession(accountId).catch(() => {}), 2000);
      }
    }
  });

  // Forward incoming messages to the API for the unified inbox.
  sock.ev.on("messages.upsert", async ({ messages, type }) => {
    if (type !== "notify") return;
    for (const m of messages) {
      if (m.key.fromMe || !m.message) continue;
      const from = (m.key.remoteJid || "").split("@")[0];
      const body = m.message.conversation || m.message.extendedTextMessage?.text || "";
      await pushEvent("message", accountId, { from, body, waMessageId: m.key.id, timestamp: m.messageTimestamp });
    }
  });

  // Forward delivery/read receipts to update campaign + inbox statuses.
  sock.ev.on("messages.update", async (updates) => {
    for (const u of updates) {
      const status = mapReceipt(u.update?.status);
      if (!status) continue;
      await pushEvent("message_status", accountId, { waMessageId: u.key?.id, status });
    }
  });

  return { status: "connecting" };
}

// Maps Baileys numeric message status to the API's vocabulary.
export function mapReceipt(status) {
  switch (status) {
    case 2: return "sent";
    case 3: return "delivered";
    case 4: return "read";
    case 0: return "failed";
    default: return null;
  }
}

export function getQr(accountId) {
  const entry = sessions.get(accountId);
  if (!entry?.qr) return null;
  if (entry.qr.expiresAt && entry.qr.expiresAt < Date.now()) return null;
  return entry.qr;
}

export async function stopSession(accountId, { wipe = false } = {}) {
  const entry = sessions.get(accountId);
  if (entry?.sock) {
    try {
      if (wipe) await entry.sock.logout();
      else entry.sock.end(undefined);
    } catch { /* ignore */ }
  }
  sessions.delete(accountId);
  if (wipe) rmSync(sessionDir(accountId), { recursive: true, force: true });
}

export function sessionStatus(accountId) {
  return sessions.get(accountId)?.status ?? "disconnected";
}

export function sessionCount() {
  return sessions.size;
}

// Sends a text or media message. Returns the WhatsApp message id.
export async function sendMessage(accountId, { to, text, mediaUrl, mediaType }) {
  const entry = sessions.get(accountId);
  if (!entry?.sock || entry.status !== "connected") {
    throw new Error("session not connected");
  }
  const jid = normalizeJid(to);

  // Verify the recipient is actually on WhatsApp so a bad/unregistered number
  // fails with a clear reason instead of silently going nowhere.
  try {
    const results = await entry.sock.onWhatsApp(jid);
    const exists = Array.isArray(results) && results.some((r) => r?.exists);
    if (!exists) throw new Error("recipient is not on WhatsApp");
  } catch (err) {
    if (err.message === "recipient is not on WhatsApp") throw err;
    // If the check itself fails (network), fall through and attempt the send.
  }

  const content = buildContent({ text, mediaUrl, mediaType });
  const sent = await entry.sock.sendMessage(jid, content);
  return sent?.key?.id ?? null;
}

// Builds a Baileys message content object from the API's send payload.
export function buildContent({ text, mediaUrl, mediaType }) {
  const type = (mediaType || "None").toLowerCase();
  if (mediaUrl && type === "image") return { image: { url: mediaUrl }, caption: text || undefined };
  if (mediaUrl && type === "video") return { video: { url: mediaUrl }, caption: text || undefined };
  if (mediaUrl && type === "document") return { document: { url: mediaUrl }, caption: text || undefined };
  if (mediaUrl && type === "audio") return { audio: { url: mediaUrl } };
  return { text: text || "" };
}
