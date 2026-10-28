// Configuration for the WhatsApp bridge, resolved from environment variables.
// All values have safe local-dev defaults so the bridge runs out of the box.

export const config = {
  // Port the bridge HTTP server listens on. Render injects PORT.
  port: parseInt(process.env.PORT || process.env.BRIDGE_PORT || "3001", 10),

  // Base URL of the .NET API that receives callbacks (qr/connected/message/...).
  // In Render this is the internal URL of the web service.
  apiBaseUrl: (process.env.API_BASE_URL || "http://localhost:5188").replace(/\/+$/, ""),

  // Shared secret sent to the API as X-Bridge-Key and required on inbound
  // requests from the API. MUST match the API's Bridge__Key.
  bridgeKey: process.env.BRIDGE_KEY || "dev-bridge-key",

  // Where Baileys auth state (multi-file) is persisted, one folder per account.
  // On Render, mount a persistent disk here so sessions survive restarts.
  sessionsDir: process.env.SESSIONS_DIR || "./sessions",

  // Max seconds a QR stays valid before the client should request a fresh one.
  qrTtlSeconds: parseInt(process.env.QR_TTL_SECONDS || "60", 10),
};
