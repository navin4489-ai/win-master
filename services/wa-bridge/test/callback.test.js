import { test } from "node:test";
import assert from "node:assert/strict";
import { pushEvent } from "../src/callback.js";

test("pushEvent posts the correct shape with the bridge key header", async () => {
  const calls = [];
  const original = globalThis.fetch;
  globalThis.fetch = async (url, opts) => {
    calls.push({ url, opts });
    return { ok: true, status: 200 };
  };
  try {
    await pushEvent("connected", 7, { phoneNumber: "+15550100001" });
    assert.equal(calls.length, 1);
    assert.match(calls[0].url, /\/api\/bridge\/events$/);
    assert.equal(calls[0].opts.method, "POST");
    assert.ok(calls[0].opts.headers["X-Bridge-Key"]);
    const body = JSON.parse(calls[0].opts.body);
    assert.equal(body.type, "connected");
    assert.equal(body.accountId, 7);
    assert.equal(body.payload.phoneNumber, "+15550100001");
  } finally {
    globalThis.fetch = original;
  }
});

test("pushEvent never throws when the API is unreachable", async () => {
  const original = globalThis.fetch;
  globalThis.fetch = async () => {
    throw new Error("ECONNREFUSED");
  };
  try {
    await assert.doesNotReject(() => pushEvent("message", 1, { body: "hi" }));
  } finally {
    globalThis.fetch = original;
  }
});
