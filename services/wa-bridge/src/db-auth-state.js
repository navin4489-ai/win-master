import { initAuthCreds, BufferJSON, proto } from "@whiskeysockets/baileys";
import { config } from "./config.js";
import { logger } from "./logger.js";

// A Baileys auth-state provider backed by the .NET API (Postgres) instead of
// local files. This makes WhatsApp sessions survive bridge restarts even on
// hosts without a persistent disk (e.g. Render free tier).
//
// Mirrors useMultiFileAuthState's contract: creds + a keyed store, where every
// value is JSON-serialized with Baileys' BufferJSON reviver/replacer.

const headers = () => ({
  "Content-Type": "application/json",
  "X-Bridge-Key": config.bridgeKey,
});

function stateUrl(accountId) {
  return `${config.apiBaseUrl}/api/bridge/sessions/${accountId}/state`;
}

async function loadAll(accountId) {
  const res = await fetch(stateUrl(accountId), { headers: headers() });
  if (!res.ok) return {};
  const rows = await res.json();
  const map = {};
  for (const r of rows) map[r.key] = r.value;
  return map;
}

async function putEntry(accountId, key, value) {
  await fetch(stateUrl(accountId), {
    method: "PUT",
    headers: headers(),
    body: JSON.stringify({ key, value }),
  }).catch((e) => logger.warn({ accountId, key, err: e.message }, "state put failed"));
}

async function clearEntry(accountId, key) {
  const url = `${stateUrl(accountId)}?stateKey=${encodeURIComponent(key)}`;
  await fetch(url, { method: "DELETE", headers: headers() }).catch(() => {});
}

export async function clearDbAuthState(accountId) {
  await fetch(stateUrl(accountId), { method: "DELETE", headers: headers() }).catch(() => {});
}

// Builds a useMultiFileAuthState-compatible { state, saveCreds } backed by the API.
export async function useDbAuthState(accountId) {
  const raw = await loadAll(accountId);
  const decode = (s) => JSON.parse(s, BufferJSON.reviver);
  const encode = (v) => JSON.stringify(v, BufferJSON.replacer);

  const creds = raw["creds"] ? decode(raw["creds"]) : initAuthCreds();

  const keys = {
    get: async (type, ids) => {
      const data = {};
      for (const id of ids) {
        const k = `${type}-${id}`;
        let val = raw[k] ? decode(raw[k]) : undefined;
        if (type === "app-state-sync-key" && val) {
          val = proto.Message.AppStateSyncKeyData.fromObject(val);
        }
        if (val !== undefined) data[id] = val;
      }
      return data;
    },
    set: async (data) => {
      const tasks = [];
      for (const type in data) {
        for (const id in data[type]) {
          const k = `${type}-${id}`;
          const value = data[type][id];
          if (value) {
            raw[k] = encode(value);
            tasks.push(putEntry(accountId, k, raw[k]));
          } else {
            delete raw[k];
            tasks.push(clearEntry(accountId, k));
          }
        }
      }
      await Promise.allSettled(tasks);
    },
  };

  const saveCreds = async () => {
    raw["creds"] = encode(creds);
    await putEntry(accountId, "creds", raw["creds"]);
  };

  return { state: { creds, keys }, saveCreds };
}
