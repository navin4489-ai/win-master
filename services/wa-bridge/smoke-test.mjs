// Ad-hoc end-to-end smoke test for the bridge (not shipped).
// Starts a mock API to capture callbacks, then drives the bridge's HTTP API.
import http from "node:http";

const BRIDGE = "http://localhost:3011";
const KEY = "test-key";
const received = [];

// Mock .NET API: capture POST /api/bridge/events.
const mock = http.createServer((req, res) => {
  if (req.method === "POST" && req.url === "/api/bridge/events") {
    let body = "";
    req.on("data", (c) => (body += c));
    req.on("end", () => {
      const ev = JSON.parse(body);
      received.push(ev);
      console.log(`[mock] event: type=${ev.type} account=${ev.accountId} qrPng=${ev.payload?.qrPngBase64 ? ev.payload.qrPngBase64.length + " chars" : "none"}`);
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ received: true }));
    });
  } else {
    res.writeHead(404);
    res.end();
  }
});

await new Promise((r) => mock.listen(5199, r));
console.log("[mock] listening on :5199");

// Start the bridge as a child configured to call our mock.
process.env.PORT = "3011";
process.env.API_BASE_URL = "http://localhost:5199";
process.env.BRIDGE_KEY = KEY;
process.env.SESSIONS_DIR = "./.test-sessions";
await import("./src/server.js");
await new Promise((r) => setTimeout(r, 1500));

// Trigger connect; a real QR should arrive via callback within a few seconds.
const res = await fetch(`${BRIDGE}/sessions/999/connect`, {
  method: "POST",
  headers: { "Content-Type": "application/json", "X-Bridge-Key": KEY },
  body: "{}",
});
console.log("[test] connect status:", res.status, await res.json());

// Wait for the qr callback.
const deadline = Date.now() + 20000;
while (Date.now() < deadline) {
  const qr = received.find((e) => e.type === "qr");
  if (qr) {
    const ok = typeof qr.payload.qrData === "string" && qr.payload.qrData.includes(",") && qr.payload.qrPngBase64?.length > 100;
    console.log(ok ? "\nPASS: real QR generated (multi-part ref + PNG)" : "\nFAIL: QR shape unexpected");
    console.log("qrData prefix:", qr.payload.qrData.slice(0, 40));
    process.exit(ok ? 0 : 1);
  }
  await new Promise((r) => setTimeout(r, 500));
}
console.log("\nFAIL: no qr callback within timeout");
process.exit(1);
