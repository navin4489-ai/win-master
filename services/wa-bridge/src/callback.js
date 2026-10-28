import { config } from "./config.js";
import { logger } from "./logger.js";

// Posts a single event to the .NET API's bridge callback endpoint.
// The API validates the X-Bridge-Key header and routes by event `type`.
// Failures are logged but never thrown: a dropped callback must not crash a session.
export async function pushEvent(type, accountId, payload) {
  const url = `${config.apiBaseUrl}/api/bridge/events`;
  const body = JSON.stringify({ type, accountId, payload: payload ?? {} });
  try {
    const res = await fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Bridge-Key": config.bridgeKey,
      },
      body,
    });
    if (!res.ok) {
      logger.warn({ type, accountId, status: res.status }, "callback rejected by API");
    }
  } catch (err) {
    logger.warn({ type, accountId, err: err.message }, "callback to API failed");
  }
}
