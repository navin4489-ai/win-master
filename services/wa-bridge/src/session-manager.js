import { config } from "./config.js";
import { logger } from "./logger.js";
import { pushEvent } from "./callback.js";

// In-memory registry of live sessions keyed by accountId.
// Each entry: { sock, status, connecting, qr }.
const sessions = new Map();

// Reconnect backoff tracking keyed by accountId. Prevents an endless tight reconnect loop
// (which stresses WhatsApp and risks a ban) by using exponential backoff + a retry cap.
const reconnectAttempts = new Map();
const MAX_RECONNECT_ATTEMPTS = 6; // 2s,4s,8s,16s,32s,60s then give up until manual Connect

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

// Cached WhatsApp protocol version. fetchLatestBaileysVersion() is a network call
// to WhatsApp and is the single biggest source of QR-generation latency (and can hang
// on restricted egress), so we fetch it once with a hard timeout and otherwise fall back
// to a known-good baked-in version. This guarantees makeWASocket is reached quickly and
// a QR is always produced.
let _waVersion = null;
let _waVersionAt = 0;
// Known-good fallback if the live version fetch is slow/blocked. Baileys tolerates a
// slightly-behind version for linking; it just needs to be present so the socket starts.
const FALLBACK_WA_VERSION = [2, 3000, 1015901307];
async function getWaVersion(b) {
  const sixHours = 6 * 60 * 60 * 1000;
  if (_waVersion && Date.now() - _waVersionAt < sixHours) return _waVersion;
  try {
    const fetchP = b.fetchLatestBaileysVersion();
    const timeoutP = new Promise((_, rej) => setTimeout(() => rej(new Error("version fetch timeout")), 6000));
    const { version } = await Promise.race([fetchP, timeoutP]);
    _waVersion = version;
    _waVersionAt = Date.now();
  } catch {
    // Never let a slow/blocked version fetch stop QR generation.
    if (!_waVersion) { _waVersion = FALLBACK_WA_VERSION; _waVersionAt = Date.now(); }
  }
  return _waVersion;
}

// Pre-warms the heavy Baileys import and the WA version at bridge boot so the first
// user QR request doesn't pay the cold-start cost (import + version fetch).
export async function warmup() {
  try {
    const { b } = await deps();
    await getWaVersion(b);
    logger.info("baileys warmed up");
  } catch (err) {
    logger.warn({ err: err.message }, "baileys warmup failed (will retry on first connect)");
  }
}

export function normalizeJid(phone) {
  let digits = String(phone).replace(/[^0-9]/g, "");
  // Strip a trunk "0" from an 11-digit local number (0XXXXXXXXXX).
  if (digits.length === 11 && digits.startsWith("0")) digits = digits.slice(1);
  // Safety net: a bare 10-digit Indian mobile (starts 6-9) with no country code would be
  // rejected by WhatsApp as "not on WhatsApp". Default it to +91 so imported/pasted local
  // numbers still reach the recipient. Numbers that already include a country code (>10
  // digits) are left untouched.
  if (digits.length === 10 && "6789".includes(digits[0])) digits = "91" + digits;
  return `${digits}@s.whatsapp.net`;
}

// Starts (or restarts) a Baileys socket for an account and wires its lifecycle.
// Safe to call repeatedly; a connection already in progress is reused.
// Pass { reset: true } to wipe any stale/partial credentials first so a fresh QR is
// guaranteed (used when a session is stuck and never produces a QR).
export async function startSession(accountId, { reset = false } = {}) {
  if (reset) {
    const cur = sessions.get(accountId);
    try { cur?.sock?.end?.(undefined); } catch { /* ignore */ }
    sessions.delete(accountId);
    try {
      const { clearDbAuthState } = await import("./db-auth-state.js");
      await clearDbAuthState(accountId);
    } catch { /* ignore */ }
  }
  const existing = sessions.get(accountId);
  // A truly live session is reused. A session that is mid-handshake — still "connecting"
  // recently, OR already showing a QR waiting to be scanned — is ALSO reused so we never
  // interrupt an in-flight QR. Only a genuinely stale entry (old, with no QR and no progress)
  // is reset so this call can produce a fresh QR instead of hanging on "connecting" forever.
  if (existing?.status === "connected") return { status: "connected" };
  if (existing?.status === "qr" && existing?.qr && existing.qr.expiresAt > Date.now()) {
    return { status: "qr" };
  }
  if (existing?.connecting && existing?.sock && existing?.startedAt && Date.now() - existing.startedAt < 90_000) {
    return { status: existing.status };
  }
  if (existing) {
    try { existing.sock?.end?.(undefined); } catch { /* ignore */ }
    sessions.delete(accountId);
  }

  const { b, QRCode } = await deps();
  const { makeWASocket, DisconnectReason, Browsers } = b;
  const { Boom } = await import("@hapi/boom");
  const { useDbAuthState, clearDbAuthState } = await import("./db-auth-state.js");

  // Auth state is persisted in the DB (via the API) so sessions survive restarts.
  const { state, saveCreds } = await useDbAuthState(accountId);
  const version = await getWaVersion(b); // cached/fallback → fast QR
  const hasCreds = !!state.creds?.registered;
  logger.info({ accountId, hasCreds, version }, "startSession: creating socket");

  let sock;
  try {
    sock = makeWASocket({
      version,
      auth: state,
      printQRInTerminal: false,
      browser: Browsers.appropriate("Chrome"),
      markOnlineOnConnect: false,
      syncFullHistory: false,
    });
  } catch (err) {
    sessions.delete(accountId);
    logger.error({ accountId, err: err.message }, "startSession: makeWASocket failed");
    throw err;
  }

  sessions.set(accountId, { sock, status: "connecting", connecting: true, qr: null, startedAt: Date.now() });
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
      if (entry) { entry.status = "connected"; entry.connecting = false; entry.qr = null; entry.retries = 0; }
      reconnectAttempts.delete(accountId); // a clean open resets the backoff
      const phoneNumber = sock.user?.id?.split(":")[0] ?? null;
      await pushEvent("connected", accountId, { phoneNumber });
      logger.info({ accountId, phoneNumber }, "session connected");
    }

    if (connection === "close") {
      const code = new Boom(lastDisconnect?.error)?.output?.statusCode;
      const loggedOut = code === DisconnectReason.loggedOut;
      sessions.delete(accountId);
      if (loggedOut) {
        await clearDbAuthState(accountId);
        reconnectAttempts.delete(accountId);
        await pushEvent("disconnected", accountId, { reason: "logged_out" });
        logger.warn({ accountId }, "logged out, credentials cleared");
      } else {
        // Baileys disconnect codes that indicate WhatsApp restricted/banned the number
        // (vs. a transient network drop). Surface them so the API can flag the account
        // and pause campaigns instead of reconnect-looping into a ban.
        const restrictionCodes = new Set([
          DisconnectReason.forbidden,        // 403 — often "temporarily banned"
          DisconnectReason.badSession,       // 500 — session rejected
          DisconnectReason.multideviceMismatch,
          DisconnectReason.badAuth,
          DisconnectReason.contentPrivacy     // newer privacy/quality lock
        ].filter((x) => x !== undefined));

        // Code 440 = stream conflict ("replaced"): this SAME WhatsApp account is linked/open in
        // another session (another device, browser tab, or a second bridge instance). Reconnecting
        // immediately just kicks the other session off — which reconnects and kicks us off — an
        // endless fight that sends nothing and stresses the number. Do NOT auto-reconnect on a
        // conflict; report it so the user closes the other session, then reconnects once.
        const isConflict = code === DisconnectReason.connectionReplaced || code === 440;

        if (isConflict) {
          reconnectAttempts.delete(accountId);
          await pushEvent("disconnected", accountId, { reason: "conflict", code });
          logger.warn({ accountId, code }, "session replaced by another WhatsApp connection — not reconnecting (close the other session, then Connect again)");
        } else if (restrictionCodes.has(code)) {
          reconnectAttempts.delete(accountId);
          await pushEvent("account_restricted", accountId, { code, reason: "restricted" });
          logger.warn({ accountId, code }, "account restricted/banned by WhatsApp");
        } else {
          // Transient drop: reconnect with exponential backoff and a retry cap so a persistent
          // failure can't hammer WhatsApp every 2s (which itself looks abusive and risks a ban).
          const attempts = (reconnectAttempts.get(accountId) ?? 0) + 1;
          if (attempts > MAX_RECONNECT_ATTEMPTS) {
            reconnectAttempts.delete(accountId);
            await pushEvent("disconnected", accountId, { reason: "unreachable", code });
            logger.warn({ accountId, code, attempts: attempts - 1 }, "giving up reconnecting after repeated failures");
          } else {
            reconnectAttempts.set(accountId, attempts);
            const delayMs = Math.min(2000 * 2 ** (attempts - 1), 60000); // 2s,4s,8s… capped 60s
            logger.warn({ accountId, code, attempt: attempts, delayMs }, "connection closed, reconnecting");
            setTimeout(() => startSession(accountId).catch(() => {}), delayMs);
          }
        }
      }
    }
  });

  // Forward incoming 1:1 text messages to the API for the unified inbox + bot.
  sock.ev.on("messages.upsert", async ({ messages, type }) => {
    if (type !== "notify") return;
    for (const m of messages) {
      if (m.key.fromMe || !m.message) continue;
      const jid = m.key.remoteJid || "";
      // Ignore status broadcasts and group chats (the 1:1 bot only answers direct chats).
      if (jid === "status@broadcast" || jid.endsWith("@g.us") || jid.endsWith("@broadcast")) continue;

      const text = extractText(m.message);
      if (!text) continue; // nothing actionable (reaction, sticker, empty media, etc.)

      const from = jid.split("@")[0];
      await pushEvent("message", accountId, { from, body: text, waMessageId: m.key.id, timestamp: m.messageTimestamp });
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
  if (wipe) {
    const { clearDbAuthState } = await import("./db-auth-state.js");
    await clearDbAuthState(accountId);
  }
}

export function sessionStatus(accountId) {
  return sessions.get(accountId)?.status ?? "disconnected";
}

export function sessionCount() {
  return sessions.size;
}

// On startup, restore every account that has persisted creds in the DB so
// linked sessions survive restarts without a re-scan.
export async function restoreSessions() {
  try {
    const res = await fetch(`${config.apiBaseUrl}/api/bridge/sessions`, {
      headers: { "X-Bridge-Key": config.bridgeKey },
    });
    if (!res.ok) { logger.warn({ status: res.status }, "could not list persisted sessions"); return; }
    const ids = await res.json();
    logger.info({ count: ids.length }, "restoring persisted sessions");
    for (const id of ids) {
      startSession(id).catch((e) => logger.warn({ id, err: e.message }, "restore failed"));
    }
  } catch (err) {
    logger.warn({ err: err.message }, "restoreSessions failed");
  }
}

// Sends a text or media message. Returns the WhatsApp message id.
export async function sendMessage(accountId, { to, text, mediaUrl, mediaType }) {
  const entry = sessions.get(accountId);
  if (!entry?.sock || entry.status !== "connected") {
    throw new Error("session not connected");
  }
  const jid = normalizeJid(to);

  // Verify the recipient is actually on WhatsApp so a bad/unregistered number
  // fails with a clear reason instead of silently going nowhere. Bounded so a
  // stalled check can never hang the send.
  try {
    const check = entry.sock.onWhatsApp(jid);
    const timeout = new Promise((_, rej) => setTimeout(() => rej(new Error("onWhatsApp timeout")), 8000));
    const results = await Promise.race([check, timeout]);
    const exists = Array.isArray(results) && results.some((r) => r?.exists);
    if (!exists) throw new Error("recipient is not on WhatsApp");
  } catch (err) {
    if (err.message === "recipient is not on WhatsApp") throw err;
    // If the check itself fails/times out, fall through and attempt the send.
  }

  const content = buildContent({ text, mediaUrl, mediaType });
  const sent = await entry.sock.sendMessage(jid, content);
  const id = sent?.key?.id ?? null;
  logger.info({ accountId, jid, messageId: id }, "message sent");
  return id;
}

// Pulls user-visible text from any inbound message type so the bot can respond
// to plain text, extended text, media captions, and button/list selections.
export function extractText(message) {
  if (!message) return "";
  return (
    message.conversation ||
    message.extendedTextMessage?.text ||
    message.imageMessage?.caption ||
    message.videoMessage?.caption ||
    message.documentMessage?.caption ||
    message.buttonsResponseMessage?.selectedDisplayText ||
    message.buttonsResponseMessage?.selectedButtonId ||
    message.listResponseMessage?.title ||
    message.listResponseMessage?.singleSelectReply?.selectedRowId ||
    message.templateButtonReplyMessage?.selectedDisplayText ||
    ""
  ).trim();
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
