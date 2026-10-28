// Ad-hoc end-to-end smoke test for the bridge (not shipped).
// Starts a mock API to capture callbacks, then drives the bridge's HTTP API.
import http from "node:http";

const BRIDGE = "http://localhost:3011";
const KEY = "test-key";
const received = [];
const stateStore = new Map(); // "accountId" -> Map(key->value)

function getStore(id) {
  if (!stateStore.has(id)) stateStore.set(id, new Map());
  return stateStore.get(id);
}

// Mock .NET API: capture callbacks + implement session-state persistence.
const mock = http.createServer((req, res) => {
  const json = (code, obj) => { res.writeHead(code, { "Content-Type": "application/json" }); res.end(JSON.stringify(obj)); };
  const url = req.url || "";

  if (req.method === "POST" && url === "/api/bridge/events") {
    let body = ""; req.on("data", (c) => (body += c));
    req.on("end", () => {
      const ev = JSON.parse(body); received.push(ev);
      console.log(`[mock] event: type=${ev.type} account=${ev.accountId} qrPng=${ev.payload?.qrPngBase64 ? ev.payload.qrPngBase64.length + " chars" : "none"}`);
      json(200, { received: true });
    });
    return;
  }
  // GET /api/bridge/sessions  -> account ids with creds
  if (req.method === "GET" && url === "/api/bridge/sessions") {
    const ids = [...stateStore.entries()].filter(([, m]) => m.has("creds")).map(([id]) => Number(id));
    return json(200, ids);
  }
  const m = url.match(/^\/api\/bridge\/sessions\/(\d+)\/state/);
  if (m) {
    const id = m[1]; const store = getStore(id);
    if (req.method === "GET") return json(200, [...store.entries()].map(([key, value]) => ({ key, value })));
    if (req.method === "PUT") {
      let body = ""; req.on("data", (c) => (body += c));
      req.on("end", () => { const e = JSON.parse(body); store.set(e.key, e.value); json(200, { saved: true }); });
      return;
    }
    if (req.method === "DELETE") {
      const sk = new URL(url, "http://x").searchParams.get("stateKey");
      if (sk) store.delete(sk); else store.clear();
      return json(200, { cleared: true });
    }
  }
  json(404, {});
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

// Wait for the qr callback. (Full auth persistence requires a real scan, which
// a headless smoke test cannot do; that is validated in the live environment.)
const deadline = Date.now() + 25000;
while (Date.now() < deadline) {
  const qr = received.find((e) => e.type === "qr");
  if (qr) {
    const qrOk = typeof qr.payload.qrData === "string" && qr.payload.qrData.includes(",") && qr.payload.qrPngBase64?.length > 100;
    console.log(qrOk ? "\nPASS: real QR generated via DB-auth provider" : "\nFAIL: qr shape");
    console.log("qrData prefix:", qr.payload.qrData.slice(0, 40));
    process.exit(qrOk ? 0 : 1);
  }
  await new Promise((r) => setTimeout(r, 500));
}
console.log("\nFAIL: no qr callback within timeout");
process.exit(1);
