# Win Master — WhatsApp Bridge (Baileys)

Out-of-process Node service that owns the real WhatsApp Web connections. It
generates scannable QR codes, keeps sessions alive, sends outbound messages,
and pushes lifecycle/inbound events back to the .NET API.

## How it fits together

```
Angular UI ──> .NET API ──POST /sessions/{id}/connect──> wa-bridge ──> WhatsApp
                  ^                                           │
                  └──POST /api/bridge/events (qr/connected)───┘
```

- The API calls the bridge to start/stop sessions and to send messages.
- The bridge calls the API's `POST /api/bridge/events` with the real QR
  (`qrData` + `qrPngBase64`), `connected`, `disconnected`, `message`, and
  `message_status` events.
- Both sides authenticate with the shared `BRIDGE_KEY` (`X-Bridge-Key` header).

## Environment variables

| Var             | Default                  | Purpose                                         |
| --------------- | ------------------------ | ----------------------------------------------- |
| `PORT`          | `3001`                   | HTTP port the bridge listens on                 |
| `API_BASE_URL`  | `http://localhost:5188`  | Base URL of the .NET API for callbacks          |
| `BRIDGE_KEY`    | `dev-bridge-key`         | Shared secret; must equal the API's `Bridge__Key` |
| `SESSIONS_DIR`  | `./sessions`             | Where Baileys auth state is persisted           |
| `QR_TTL_SECONDS`| `60`                     | QR validity hint                                |

## Run locally

```bash
cd services/wa-bridge
npm install
API_BASE_URL=http://localhost:5188 BRIDGE_KEY=dev-bridge-key npm start
```

Then in the API set `Bridge__BaseUrl=http://localhost:3001` and
`Bridge__Key=dev-bridge-key` (these are the defaults).

## HTTP endpoints (called by the API)

- `POST /sessions/{id}/connect` — start/reconnect a session (QR arrives via callback)
- `POST /sessions/{id}/disconnect?wipe=true` — stop; `wipe` unlinks + clears creds
- `GET  /sessions/{id}/status` — `connecting | qr | connected | disconnected`
- `POST /sessions/{id}/send` — `{ to, text, mediaUrl?, mediaType? }` → `{ messageId }`
- `GET  /healthz` — health check (no auth)

## Persistence

Auth state lives under `SESSIONS_DIR/{accountId}`. Mount a persistent disk
there in production so linked devices survive restarts. Logging out (or the
user unlinking from their phone) clears that account's folder, forcing a
fresh QR on next connect.
