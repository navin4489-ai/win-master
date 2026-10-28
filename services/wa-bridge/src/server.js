import express from "express";
import { config } from "./config.js";
import { logger } from "./logger.js";
import {
  startSession,
  stopSession,
  sessionStatus,
  sendMessage,
  getQr,
  sessionCount,
} from "./session-manager.js";

const app = express();
app.use(express.json({ limit: "2mb" }));

// Health check (unauthenticated) for container orchestration.
app.get("/healthz", (_req, res) => res.json({ status: "healthy", sessions: sessionCount() }));

// All session routes require the shared bridge key. This is server-to-server
// only; the API holds the same key in Bridge__Key.
app.use((req, res, next) => {
  if (req.path === "/healthz") return next();
  const key = req.get("X-Bridge-Key");
  if (key !== config.bridgeKey) return res.status(401).json({ error: "unauthorized" });
  next();
});

function accountId(req) {
  const id = parseInt(req.params.id, 10);
  if (!Number.isInteger(id)) throw new Error("invalid account id");
  return id;
}

// Start a session: begins the QR handshake (or reconnects with saved creds).
// The actual QR/connected result arrives asynchronously via the callback.
app.post("/sessions/:id/connect", async (req, res) => {
  try {
    const result = await startSession(accountId(req));
    res.json(result);
  } catch (err) {
    logger.error({ err: err.message }, "connect failed");
    res.status(400).json({ error: err.message });
  }
});

app.post("/sessions/:id/disconnect", async (req, res) => {
  try {
    // wipe=true fully unlinks the device and clears stored credentials.
    const wipe = req.query.wipe === "true" || req.body?.wipe === true;
    await stopSession(accountId(req), { wipe });
    res.json({ ok: true });
  } catch (err) {
    res.status(400).json({ error: err.message });
  }
});

app.get("/sessions/:id/status", (req, res) => {
  try {
    res.json({ status: sessionStatus(accountId(req)) });
  } catch (err) {
    res.status(400).json({ error: err.message });
  }
});

// Returns the current QR (data + PNG base64) if one is pending for this session.
app.get("/sessions/:id/qr", (req, res) => {
  try {
    const qr = getQr(accountId(req));
    if (!qr) return res.status(404).json({ error: "no qr available" });
    res.json({
      qrData: qr.qrData,
      qrPngBase64: qr.qrPngBase64,
      expiresAt: new Date(qr.expiresAt).toISOString(),
    });
  } catch (err) {
    res.status(400).json({ error: err.message });
  }
});

app.post("/sessions/:id/send", async (req, res) => {
  try {
    const { to, text, mediaUrl, mediaType } = req.body ?? {};
    const messageId = await sendMessage(accountId(req), { to, text, mediaUrl, mediaType });
    res.json({ messageId });
  } catch (err) {
    logger.warn({ err: err.message }, "send failed");
    res.status(400).json({ error: err.message });
  }
});

app.listen(config.port, () => {
  logger.info(
    { port: config.port, apiBaseUrl: config.apiBaseUrl },
    "wa-bridge listening"
  );
});
