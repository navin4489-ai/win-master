import { mkdirSync, rmSync } from "node:fs";
import { join } from "node:path";
import {
  makeWASocket,
  useMultiFileAuthState,
  fetchLatestBaileysVersion,
  DisconnectReason,
  Browsers,
} from "@whiskeysockets/baileys";
import { Boom } from "@hapi/boom";
import QRCode from "qrcode";
import { config } from "./config.js";
import { logger } from "./logger.js";
import { pushEvent } from "./callback.js";

// In-memory registry of live sessions keyed by accountId.
// Each entry: { sock, status, connecting }.
const sessions = new Map();

function sessionDir(accountId) {
  return join(config.sessionsDir, String(accountId));
}

function normalizeJid(phone) {
  const digits = String(phone).replace(/[^0-9]/g, "");
  return `${digits}@s.whatsapp.net`;
}

// Starts (or restarts) a Baileys socket for an account and wires its lifecycle:
//   - connection.update emits qr / connected / disconnected callbacks
//   - creds.update persists auth state so re-scans aren't needed after restart
// Safe to call repeatedly; a connection already in progress is reused.
export async function startSession(accountId) {
  const existing = sessions.get(accountId);
  if (existing?.connecting || existing?.status === "connected") {
    return { status: existing.status };
  }

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

  sessions.set(accountId, { sock, status: "connecting", connecting: true });

  sock.ev.on("creds.update", saveCreds);

  sock.ev.on("connection.update", async (update) => {
    const { connection, lastDisconnect, qr } = update;

    if (qr) {
      try {
        const qrPngDataUrl = await QRCode.toDataURL(qr, { margin: 1, width: 320 });
        // Strip the data-URL prefix; the API stores raw base64.
        const qrPngBase64 = qrPngDataUrl.replace(/^data:image\/png;base64,/, "");
        const entry = sessions.get(accountId);
        if (entry) entry.status = "qr";
        await pushEvent("qr", accountId, { qrData: qr, qrPngBase64 });
        logger.info({ accountId }, "qr generated");
      } catch (err) {
        logger.error({ accountId, err: err.message }, "failed to render qr");
      }
    }

    if (connection === "open") {
      const entry = sessions.get(accountId);
      if (entry) {
        entry.status = "connected";
        entry.connecting = false;
      }
      const phoneNumber = sock.user?.id?.split(":")[0] ?? null;
      await pushEvent("connected", accountId, { phoneNumber });
      logger.info({ accountId, phoneNumber }, "session connected");
    }

    if (connection === "close") {
      const code = new Boom(lastDisconnect?.error)?.output?.statusCode;
      const loggedOut = code === DisconnectReason.loggedOut;
      const entry = sessions.get(accountId);
      if (entry) {
        entry.status = "disconnected";
        entry.connecting = false;
      }
      sessions.delete(accountId);

      if (loggedOut) {
        // Device unlinked from the phone: wipe creds so a fresh QR is required.
        rmSync(sessionDir(accountId), { recursive: true, force: true });
        await pushEvent("disconnected", accountId, { reason: "logged_out" });
        logger.warn({ accountId }, "logged out, credentials cleared");
      } else {
        // Transient drop: reconnect automatically, reusing saved creds.
        logger.warn({ accountId, code }, "connection closed, reconnecting");
        setTimeout(() => startSession(accountId).catch(() => {}), 2000);
      }
    }
  });

  return { status: "connecting" };
}

export async function stopSession(accountId, { wipe = false } = {}) {
  const entry = sessions.get(accountId);
  if (entry?.sock) {
    try {
      if (wipe) await entry.sock.logout();
      else entry.sock.end(undefined);
    } catch {
      /* ignore */
    }
  }
  sessions.delete(accountId);
  if (wipe) rmSync(sessionDir(accountId), { recursive: true, force: true });
}

export function sessionStatus(accountId) {
  return sessions.get(accountId)?.status ?? "disconnected";
}

// Sends a text or media message. Returns the WhatsApp message id.
export async function sendMessage(accountId, { to, text, mediaUrl, mediaType }) {
  const entry = sessions.get(accountId);
  if (!entry?.sock || entry.status !== "connected") {
    throw new Error("session not connected");
  }
  const jid = normalizeJid(to);

  let content;
  const type = (mediaType || "None").toLowerCase();
  if (mediaUrl && type === "image") content = { image: { url: mediaUrl }, caption: text || undefined };
  else if (mediaUrl && type === "video") content = { video: { url: mediaUrl }, caption: text || undefined };
  else if (mediaUrl && type === "document") content = { document: { url: mediaUrl }, caption: text || undefined };
  else if (mediaUrl && type === "audio") content = { audio: { url: mediaUrl } };
  else content = { text: text || "" };

  const sent = await entry.sock.sendMessage(jid, content);
  return sent?.key?.id ?? null;
}
