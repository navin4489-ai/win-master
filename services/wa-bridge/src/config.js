// Configuration for the WhatsApp bridge, resolved from environment variables.
// All values have safe local-dev defaults so the bridge runs out of the box.

function normalizeUrl(value) {
  value = String(value).trim().replace(/\/+$/, "");
  if (!/^https?:\/\//i.test(value)) value = "http://" + value;
  return value;
}

export const config = {
  // Port the bridge HTTP server listens on. Prefer BRIDGE_PORT so a platform's
  // injected PORT (meant for the API) never hijacks the bridge when co-hosted.
  port: parseInt(process.env.BRIDGE_PORT || process.env.PORT || "3001", 10),

  // Base URL of the .NET API that receives callbacks (qr/connected/message/...).
  // In Render this is the internal URL of the web service. Render's fromService
  // hostport yields "host:port" with no scheme, so default to http://.
  apiBaseUrl: normalizeUrl(process.env.API_BASE_URL || "http://localhost:5188"),

  // Shared secret sent to the API as X-Bridge-Key and required on inbound
  // requests from the API. MUST match the API's Bridge__Key.
  bridgeKey: process.env.BRIDGE_KEY || "dev-bridge-key",

  // Where Baileys auth state (multi-file) is persisted, one folder per account.
  // On Render, mount a persistent disk here so sessions survive restarts.
  sessionsDir: process.env.SESSIONS_DIR || "./sessions",

  // Max seconds a QR stays valid before the client should request a fresh one.
  qrTtlSeconds: parseInt(process.env.QR_TTL_SECONDS || "60", 10),
};
