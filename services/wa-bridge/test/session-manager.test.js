import { test } from "node:test";
import assert from "node:assert/strict";
import { normalizeJid, mapReceipt, buildContent, extractText } from "../src/session-manager.js";

test("extractText reads plain, extended, caption and button/list replies", () => {
  assert.equal(extractText({ conversation: "hi" }), "hi");
  assert.equal(extractText({ extendedTextMessage: { text: "hello there" } }), "hello there");
  assert.equal(extractText({ imageMessage: { caption: "nice pic" } }), "nice pic");
  assert.equal(extractText({ buttonsResponseMessage: { selectedDisplayText: "Yes" } }), "Yes");
  assert.equal(extractText({ listResponseMessage: { title: "Option A" } }), "Option A");
  assert.equal(extractText({ stickerMessage: {} }), "");
  assert.equal(extractText(null), "");
});


test("normalizeJid strips non-digits and appends the WhatsApp suffix", () => {
  assert.equal(normalizeJid("+1 (555) 010-0001"), "15550100001@s.whatsapp.net");
  assert.equal(normalizeJid("919876543210"), "919876543210@s.whatsapp.net");
});

test("mapReceipt maps Baileys status codes to the API vocabulary", () => {
  assert.equal(mapReceipt(2), "sent");
  assert.equal(mapReceipt(3), "delivered");
  assert.equal(mapReceipt(4), "read");
  assert.equal(mapReceipt(0), "failed");
  assert.equal(mapReceipt(99), null);
});

test("buildContent produces text when no media", () => {
  assert.deepEqual(buildContent({ text: "hello" }), { text: "hello" });
});

test("buildContent produces image with caption", () => {
  const c = buildContent({ text: "hi", mediaUrl: "http://x/y.png", mediaType: "Image" });
  assert.equal(c.image.url, "http://x/y.png");
  assert.equal(c.caption, "hi");
});

test("buildContent produces document", () => {
  const c = buildContent({ mediaUrl: "http://x/doc.pdf", mediaType: "Document" });
  assert.equal(c.document.url, "http://x/doc.pdf");
});
