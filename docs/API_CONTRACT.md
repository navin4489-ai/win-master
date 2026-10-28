# Win Master — API Contract (v1)

Single source of truth for `backend/`, `web/`, `mobile/`.
Base URL (dev): `https://localhost:7188` · Swagger: `/swagger`
All responses JSON, camelCase. Errors: RFC-7807 style `{"title","status","detail","errors"}`.
Auth: `Authorization: Bearer <accessToken>` unless marked **public**.

## Conventions

* Pagination: `?page=1&pageSize=50` → `{ items: T[], page, pageSize, totalCount, totalPages }`.
* Dates: ISO-8601 UTC (`2026-10-28T11:00:00Z`).
* Money/decimals: numbers. IDs: `int`. License keys: string.
* Roles: `User`, `Reseller`, `Admin`, `SuperAdmin`. Authorization is hierarchical: SuperAdmin ⊃ Admin ⊃ Reseller ⊃ User.

---

## 1. Auth — `/api/auth`

| Method | Path | Auth | Body / Query | Response |
|---|---|---|---|---|
| POST | `/login` | public | `{email, password}` | `{accessToken, refreshToken, expiresIn, user: UserDto}` |
| POST | `/refresh` | public | `{refreshToken}` | same as login |
| POST | `/logout` | bearer | `{refreshToken}` | 204 |
| GET | `/me` | bearer | — | `UserDto` |

`UserDto = { id, email, displayName, role, parentUserId, isActive, resellerCode?, license?: LicenseDto }`

## 2. Users — `/api/users` (Admin+)

`GET /` (filter `role`, `parentId`, paged) · `GET /{id}` · `POST` `{email, password, displayName, role, parentUserId?}` · `PUT /{id}` `{displayName, role?, isActive?}` · `DELETE /{id}` · `GET /tree` → hierarchical nodes.

**Rule:** an Admin can only create/manage Resellers and Users under themselves; Reseller can create Users only; SuperAdmin sees all.

## 3. Licenses — `/api/licenses`

```
LicenseDto = { id, key, licenseType: "User"|"Reseller"|"Admin", issuedById, userId?, userEmail?,
               features: string[], startsAt, expiresAt?, maxActivations, status: "Active"|"Revoked"|"Expired",
               createdAt, activationCount }
ActivationDto = { id, licenseId, hwid, deviceName, activatedAt, lastVerifiedAt, revokedAt? }
```

| Method | Path | Auth | Body | Response |
|---|---|---|---|---|
| GET | `/` | Reseller+ (own subtree) | `?status&type&q` paged | paged `LicenseDto` |
| POST | `/` | Reseller+ | `{licenseType, features[], startsAt, expiresAt?, maxActivations, userId?}` | `LicenseDto` (key generated) |
| POST | `/{id}/revoke` | issuer-or-above | — | `LicenseDto` |
| GET | `/{id}/activations` | Reseller+ | — | `ActivationDto[]` |
| DELETE | `/activations/{activationId}` | Reseller+ | — | 204 (device revoke) |
| POST | `/activate` | **public** | `{key, hwid, deviceName, productVersion}` | `{license: LicenseDto, token, verifyIntervalHours: 24, offlineGraceDays: 7}` |
| POST | `/verify` | **public** | `{key, hwid, token}` | `{valid, reason?, expiresAt?}` — must succeed offline-capable (signed token, 7-day grace) |
| POST | `/deactivate` | **public** | `{key, hwid}` | 204 |

## 4. WhatsApp accounts — `/api/whatsapp-accounts`

```
WaAccountDto = { id, engineType: "Baileys"|"CloudApi", phoneNumber, displayName,
                 status: "Disconnected"|"Connecting"|"QrReady"|"Connected"|"Banned"|"Error",
                 proxyId?, proxyLabel?, warmupState: "None"|"Warming"|"Ready", rotationWeight,
                 dailyLimit, sentToday, lastSeenAt?, sessionId? }
```

`GET /` · `GET /{id}` · `POST` `{engineType, phoneNumber?, displayName, proxyId?, credentials?}` · `PUT /{id}` · `DELETE /{id}` ·
`POST /{id}/connect` → `{status:"QrReady"}` · `GET /{id}/qr` → `{qrPngBase64?, qrData?, expiresAt?}` ·
`POST /{id}/disconnect` · `POST /{id}/rotate` `{rotationWeight}` · `GET /status` → aggregate `{connected, connecting, total}`.

CloudApi accounts store `credentials = {phoneNumberId, accessToken, wabaId}` (encrypted at rest).

## 5. Proxies — `/api/proxies`

`GET /` · `POST` `{type:"HTTP"|"HTTPS"|"SOCKS5", host, port, username?, password?}` · `PUT /{id}` · `DELETE /{id}` · `POST /{id}/test` → `{ok, latencyMs?, error?}`.
Proxy password is write-only in DTOs (never returned).

## 6. Contact groups — `/api/contact-groups`

```
ContactGroupDto = { id, name, source: "Import"|"GroupScrape"|"Manual", totalContacts,
                    validContacts, invalidContacts, tags: string[], createdAt, updatedAt }
ContactDto = { id, groupId, phone, name?, waRegistered: bool|null, customFields: Record<string,string>,
               tags: string[], lastMessagedAt? }
TagDto = { id, name, color }
```

| Method | Path | Notes |
|---|---|---|
| GET `/` | paged, `?q&tag` | |
| POST `/` | `{name, tags?}` | |
| PUT `/{id}` / DELETE `/{id}` | | |
| POST `/{id}/import` | `multipart/form-data` file=`.csv|.xlsx` + `columnMapJson` | returns `{imported, invalid, duplicates, errors[]}`, batched bulk insert (50k+ OK) |
| POST `/validate` | `{phones: string[]}` | returns `[{phone, valid, waRegistered, reason?}]` |
| GET `/{id}/export?format=csv\|xlsx` | stream download | |
| POST `/{id}/scrape` | `{source:"group", accountUrl|groupId}` | enqueues bridge group-scraper job → `{jobId}` |
| GET `/{id}/scrape/{jobId}` | | `{status, extracted, message?}` |
| GET `/{id}/contacts` | paged, `?q&tag&valid` | |
| POST `/{id}/contacts` / PUT `/contacts/{id}` / DELETE `/contacts/{id}` | manual CRUD | |
| GET/POST/PUT/DELETE `/tags` | `{name, color}` | |

## 7. Templates — `/api/templates`

```
TemplateDto = { id, name, body, mediaType: "None"|"Image"|"Document"|"Video"|"Audio", mediaUrl?,
                buttons?: [{type:"Reply"|"Url"|"Phone", id, title, payload?}],
                listMenu?: {title, sections:[{title, rows:[{id,title,description?}]}]},
                category, createdAt, updatedAt }
```
`GET /` · `POST` · `PUT /{id}` · `DELETE /{id}`. `body` supports `{{Var}}` and `{a|b|c}` spintax.

## 8. Campaigns — `/api/campaigns`

```
CampaignDto = { id, name, templateId?, templateName?, groupId, groupName, engineType,
                accountId? /*null = rotation*/, rotationMode: "Sequential"|"RoundRobin",
                status: "Draft"|"Scheduled"|"Running"|"Paused"|"Completed"|"Stopped"|"Failed",
                scheduledAt?, windowStart?, windowEnd?, spintaxEnabled, personalizeMessage?,
                pacing: { minDelaySec, maxDelaySec, batchSize, cooldownMinutes },
                totalRecipients, processedCount, failedCount, progressPercent,
                startedAt?, completedAt?, createdAt }
CampaignProgress = { campaignId, status, total, processed, sent, delivered, read, failed,
                     currentRecipientIndex?, estimatedSecondsRemaining?, lastCheckpointAt? }
CampaignRecipientDto = { id, phoneMasked, name?, status: "Pending"|"Sending"|"Sent"|"Delivered"|"Read"|"Failed",
                         variantText?, error?, sentAt?, deliveredAt?, readAt? }
```

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET `/` | User+ | paged, `?status&q` | |
| POST `/` | User+ | `{name, templateId?, body?, groupId, engineType, accountId?, rotationMode, scheduledAt?, windowStart?, windowEnd?, spintaxEnabled, pacing{...}}` → Draft |
| GET `/{id}` | | `CampaignDto` | |
| PUT `/{id}` | | editable only when Draft/Paused | |
| DELETE `/{id}` | | Draft/Stopped only | |
| POST `/{id}/schedule` | `{scheduledAt, windowStart?, windowEnd?}` → Scheduled | |
| POST `/{id}/start` \| `/pause` \| `/resume` \| `/stop` | state transitions | |
| GET `/{id}/progress` | `CampaignProgress` (poll; also pushed over `/api/hub` SignalR) | |
| GET `/{id}/recipients` | paged, `?status` | |
| GET `/{id}/report?format=csv\|xlsx` | file download | |

**Execution rules:** random delay `[pacing.minDelaySec, pacing.maxDelaySec]`; pause `cooldownMinutes` after every `batchSize` sends; rotation picks next eligible account; messages are idempotent per `(campaignId, phone)` — resume never re-sends a `Sent` row.

## 9. Anti-ban — `/api/anti-ban`

`GET /{accountId}` · `PUT /{accountId}` `{minDelaySec, maxDelaySec, batchSize, cooldownMinutes, dailyCap}`.
`GET /global` · `PUT /global` — account-wide defaults.

## 10. Auto-reply rules — `/api/auto-reply-rules`

```
AutoReplyRuleDto = { id, accountId, matchType: "Exact"|"Wildcard"|"Regex", pattern, priority,
                     responseText?, responseTemplateId?, useAi: bool, isActive }
```
`GET /` (`?accountId`) · `POST` · `PUT /{id}` · `DELETE /{id}` · `PUT /{id}/toggle`.
Invalid regex must be rejected at validation time (400 with `errors`).

## 11. AI settings — `/api/ai-settings`

`GET /` → `{provider:"OpenAI"|"Anthropic"|null, model, systemPrompt, isActive, dailyTokenBudget, maskedApiKey}` · `PUT /` `{provider, apiKey?, model, systemPrompt, isActive, dailyTokenBudget}` · `POST /test` `{message}` → `{reply, tokensUsed, latencyMs}`.

## 12. Inbox — `/api/inbox`

```
InboxThreadDto = { id, accountId, accountLabel, contactPhone, contactPhoneMasked, contactName?,
                   lastMessageAt, unreadCount, isBotEnabled, preview }
InboxMessageDto = { id, threadId, direction: "In"|"Out", body, mediaUrl?, mediaType?,
                    status?: "Queued"|"Sent"|"Delivered"|"Read"|"Failed", waMessageId?, sentAt, createdAt }
```

`GET /threads` (`?accountId&q&unreadOnly`, paged) · `GET /threads/{id}/messages` (paged, newest-last) ·
`POST /threads/{id}/messages` `{body, mediaUrl?}` → 201 `InboxMessageDto` ·
`PUT /threads/{id}/bot` `{isBotEnabled}` · `POST /threads/{id}/read` · `GET /accounts` (thread counts).

## 13. Reports & dashboard

`GET /api/reports/delivery?from&to&campaignId&accountId` →
`{ sent, delivered, read, failed, deliveryRate, readRate, series: [{date, sent, delivered, read, failed}] }`
`GET /api/reports/kpis?from&to` → `{deliveryRate, crashFreeSessions?, suspensionRate, totalCampaigns, totalMessages, activeAccounts, activeLicenses}`
`GET /api/dashboard/stats` → `{ campaigns: {total, running, scheduled}, messages: {sentToday, deliveredToday, failedToday},
   accounts: {connected, total}, contacts: {total, groups}, licenses: {active, expiringSoon}, inbox: {unread, threads} }`
`GET /api/audit-logs?from&to&userId&action` (paged, Admin+; `maskedDetail` only).

## 14. Integrations

* `POST /api/webhooks/meta` — **public**, Meta Graph webhook (verify `hub.challenge`, signature `X-Hub-Signature-256`), idempotent by `X-Message-Id`.
* `POST /api/bridge/events` — **public**, header `X-Bridge-Key`; body `{type: "qr"|"connected"|"disconnected"|"message"|"message_status"|"scrape_progress", accountId, payload}`.
* `POST /api/push-tokens` `{platform:"Android"|"iOS", token, deviceId}` · `DELETE /api/push-tokens/{token}`.
* `GET /api/settings` · `PUT /api/settings` (Admin+) — branding, engine URLs, retention.
* `GET /healthz` — liveness + DB check.
* SignalR hub `/api/hub` (optional, group `user:{id}`) — `campaignProgress`, `inboxMessage`, `accountStatus` events for web/mobile realtime.

## 15. Error codes

`400` validation (FluentValidation, `errors` map) · `401` bad/expired token · `403` role or hierarchy violation ·
`404` not found / not visible in your subtree · `409` duplicate (email, phone in group, unique campaign+phone) ·
`422` business rule (campaign state transition, license limit reached) · `429` rate limited · `500` unexpected.

## 16. Demo seed

`admin@winmaster.io` / `SuperAdmin` · `reseller@winmaster.io` · `user@winmaster.io` (password `Win@12345`), plus 2 groups (50 + 5 000 contacts), 2 accounts (Baileys + CloudApi), 1 proxy, 2 templates, 3 campaigns (Completed / Running / Scheduled), 5 licenses, sample inbox threads — so every screen has data on first run.
