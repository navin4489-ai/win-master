# Win Master — Detailed Implementation Plan

**Solution name:** Win Master
**Source requirements:** `Win Master.pdf` (9-page BRD, rendered to `docs/requirements/pages/*.png`)
**Approved stack:** ASP.NET Core Web API (controller-based) · Angular (web) · Flutter (mobile) · PostgreSQL
**Scope of this pass:** full scaffold of every BRD module (API + web + mobile)

---

## 1. What the BRD asks for (condensed)

**Product category:** WhatsApp Automation, Bulk Messaging & Marketing Platform (BRD authored as "BotMaster Plus"; renamed to **Win Master**).

**Personas / roles**

| Role | Goal |
|---|---|
| End-User / Marketer | Runs campaigns, schedules broadcasts, filters numbers, handles conversations via bots |
| Reseller | Generates, activates, manages downstream single-user licenses (white-label dashboard) |
| Admin | Manages a network of resellers and end-users, license issuance, batch allocations |
| Super Admin | Root access over admins, resellers, software updates, billing tiers |

**Functional requirements (BRD §4)**

| ID | Requirement |
|---|---|
| FR-1.1 | Dual engine: Baileys Web API (QR, no Meta fee) **and** WhatsApp Business/Cloud API |
| FR-1.2 | Multi-account management — authenticate, store, switch simultaneous accounts |
| FR-1.3 | Account warm-up & rotation (sequential / round-robin across numbers) |
| FR-2.1 | Dynamic templating `{{Name}}`, `{{InvoiceNo}}`, `{{CustomField}}` from CSV/Excel |
| FR-2.2 | Rich media: PDF, DOCX, JPG, PNG, MP4, audio voice notes |
| FR-2.3 | Interactive elements: CTA buttons, quick replies, list menus |
| FR-2.4 | Spintax `{Hello|Hi|Greetings}` variation across batches |
| FR-2.5 | Campaign scheduler (date + time windows) |
| FR-3.1 | Human pacing: configurable 5–15 s random delay between messages |
| FR-3.2 | Batch pauses: auto-pause after N messages for a cooldown |
| FR-3.3 | Proxy management: HTTP/HTTPS/SOCKS5 per WhatsApp account |
| FR-4.1 | Rule-based auto-reply: exact / wildcard / regex triggers |
| FR-4.2 | AI chatbot: optional OpenAI / Anthropic integration |
| FR-4.3 | Centralized inbox: incoming messages, statuses (Sent/Delivered/Read/Failed), manual reply |
| FR-4.4 | Contact & group tools: group extraction, number validation, tag segmentation |
| FR-5.1 | Key generation locked to machine fingerprint (HWID), signed license keys |
| FR-5.2 | Hierarchical license panels (Super Admin → Admin/Reseller; Admin → Reseller/User; Reseller → end user) |
| FR-5.3 | Offline / local license verification (no always-on cloud dependency) |

**Non-functional (BRD §5):** 50,000+ contacts without UI freeze; Windows 10/11 x64 client; contact data encrypted locally (SQLite), no telemetry of contact data; campaign resume from exact last-sent record with **no duplicates**; single-screen launch, progress bars, CSV/Excel exportable reports.

**Out of scope Phase 1 (BRD §3.2):** enterprise ERP, deep bidirectional Salesforce/HubSpot sync. *(Native mobile was originally out of scope; the mobile addendum (pages 5–6) re-scopes it as **Path A — Companion/Dashboard app**, which is what we build with Flutter.)*

**Mobile addendum (pages 5–6):** Flutter companion app modules = (1) live analytics & monitoring, (2) unified chat inbox with two-way messaging + auto-reply toggle, (3) contact & group scraper viewer with tagging/queueing, (4) reseller & license management (generate keys, active devices, revoke). Push via FCM. Session tokens never stored on mobile.

**KPIs:** ≥95% delivery on cleaned numbers, >99.2% crash-free sessions, ≥40% suspension reduction via pacing/rotation.

---

## 2. Target architecture

```
                        ┌──────────────────────────────────────────┐
 Angular web (admin/    │            Win Master API               │
 reseller/end-user) ───▶│  ASP.NET Core · controllers · JWT · RBAC │
                        │  EF Core + Npgsql · Swagger · Polly      │
 Flutter companion ────▶│                                          │
 app                    │  Background services:                    │
                        │   • CampaignWorker (queue, pacing,       │
 Windows desktop        │     rotation, resume, anti-ban)          │
 client (later phase) ─▶│   • SchedulerService (cron windows)      │
                        │   • BridgeListener (events from engine)  │
                        └───────┬─────────────────────┬────────────┘
                                │                     │
                    PostgreSQL ─┘                     ▼
                                    ┌──────────────────────────────────┐
                                    │ WhatsApp channel abstraction      │
                                    │  IWhatsAppChannel                 │
                                    │   ├─ MetaCloudChannel (Graph API) │
                                    │   └─ BaileysBridgeChannel ────────┼──▶ Node/Baileys
                                    └──────────────────────────────────┘      bridge service
                                                                              (QR, sockets, send,
                                                                               status webhooks)
```

**Key decisions**

1. **Controller-based API** (per approval and existing `ORO_POS_Solution` conventions): `[ApiController]` + DTOs + service layer, grouped `api/<module>`.
2. **PostgreSQL** via Npgsql/EF Core; connection string from `ConnectionStrings:DefaultConnection` or `DATABASE_URL`. A `Database:Provider` switch (`Postgres` default, `Sqlite` for local runs without a DB server) keeps dev/test possible on this machine — production stays PostgreSQL.
3. **Campaign execution is resumable**: every recipient row is a state machine (`Pending → Sending → Sent → Delivered → Read → Failed`), the worker checkpoints after each send, so a crash resumes at the exact last record without duplicates (NFR Reliability).
4. **Baileys runs out-of-process** (Node service) because Baileys is a Node library; .NET talks to it over HTTP + WebSocket and receives status callbacks. This also satisfies the BRD "automated updater for engine library" risk mitigation (engine ships independently of the UI).
5. **License keys are signed** (Ed25519/HMAC-SHA512 payload + offline grace window) and lock to an HWID; the API can also be bypassed offline per FR-5.3.
6. **No contact telemetry**: contact/message tables live in the app DB only; logs never contain phone numbers or message bodies (masked in `AuditLog`).

---

## 3. Repository layout

```
WinMaster/
├─ docs/
│  ├─ PLAN.md                     ← this file
│  ├─ API_CONTRACT.md             ← full endpoint/DTO contract (web + mobile build against it)
│  └─ requirements/pages/         ← rendered BRD pages
├─ backend/
│  ├─ WinMaster.sln
│  ├─ WinMaster.API/              ← controllers, Program.cs, Swagger, auth, workers
│  ├─ WinMaster.Core/             ← entities, enums, interfaces, DTOs, exceptions
│  ├─ WinMaster.Infrastructure/   ← DbContext, migrations, repositories, domain services
│  └─ WinMaster.Tests/            ← xUnit unit + integration tests
├─ services/
│  └─ wa-bridge/                  ← Node + Baileys bridge (QR connect, send, status callback)
├─ web/                           ← Angular app (winmaster-web)
└─ mobile/                        ← Flutter companion app (winmaster_app)
```

---

## 4. Data model (PostgreSQL)

| Table | Purpose / notable columns |
|---|---|
| `Roles` | User, Reseller, Admin, SuperAdmin |
| `Users` | email, passwordHash, displayName, roleId, parentUserId (hierarchy), isActive, resellerCode |
| `RefreshTokens` | token hash, expiry, revokedAt |
| `Machines` | hwid, userId, deviceId, lastSeenAt, revokedAt (FR-5.1, FR-5.2) |
| `Licenses` | key, licenseType (User/Reseller/Admin), issuedById, userId, features, startsAt, expiresAt, maxActivations, status |
| `Activations` | licenseId, hwid, deviceName, activatedAt, lastVerifiedAt, revokedAt |
| `WhatsAppAccounts` | engineType (Baileys/CloudApi), phoneNumber, displayName, status, sessionId, proxyId, warmupState, rotationWeight, dailyLimit |
| `Proxies` | type (HTTP/HTTPS/SOCKS5), host, port, username, passwordEnc, isHealthy |
| `ContactGroups` | name, source (Import/GroupScrape), counts |
| `Contacts` | groupId, phone (normalized), name, customFields (jsonb), tags (jsonb), waRegistered, lastMessagedAt |
| `Tags` | name, color |
| `Templates` | name, body, mediaType, mediaUrl, buttons (jsonb), listMenu (jsonb) |
| `Campaigns` | name, templateId, groupId, engineType, status (Draft/Scheduled/Running/Paused/Completed/Stopped), scheduledAt, windowStart/End, pacing config, spintaxEnabled, rotationMode, totalRecipients, processedCount, failedCount |
| `CampaignRecipients` | campaignId, contactId, phone, personalizationJson, status, variantText, sentAt, deliveredAt, readAt, error, messageId (**unique (campaignId, phone)** → duplicate guard) |
| `CampaignCheckpoints` | campaignId, lastRecipientId, updatedAt (crash recovery) |
| `AutoReplyRules` | accountId, matchType (Exact/Wildcard/Regex), pattern, responseTemplateId, responseText, isActive, priority |
| `AiSettings` | provider (OpenAI/Anthropic), apiKeyEnc, model, systemPrompt, isActive, dailyTokenBudget |
| `InboxThreads` | accountId, contactPhone, contactId, lastMessageAt, unreadCount, assignedTo, isBotEnabled |
| `InboxMessages` | threadId, direction (In/Out), body, mediaUrl, status (Sent/Delivered/Read/Failed), waMessageId, sentAt |
| `AccountAntiBanSettings` | accountId, minDelaySec, maxDelaySec, batchSize, cooldownMinutes, dailyCap |
| `WebhookEvents` | provider, externalId, payload (jsonb), processedAt (idempotent ingest) |
| `PushTokens` | userId, platform, token, deviceId (FCM) |
| `AuditLogs` | userId, action, entity, entityId, ip, maskedDetail, createdAt |
| `AppSettings` | key, valueJson (retention, branding, engine endpoints) |

**Performance:** all list endpoints paginate (default 50, keyset on `Contacts`), phone column is indexed + unique per group; contact import uses batched `COPY`-style bulk insert to clear the 50k-contact NFR.

---

## 5. Module map (full scaffold scope)

| # | Module | API | Angular | Flutter |
|---|---|---|---|---|
| M1 | Auth + RBAC (JWT, refresh, roles) | ✔ | login, guards, role menu | login, secure storage |
| M2 | Users & hierarchy (admin/reseller/end-user) | ✔ | users CRUD, tree | read-only |
| M3 | Licenses: key gen, HWID activation, revoke, devices | ✔ | license panel per role | generate/revoke keys, devices |
| M4 | WhatsApp accounts + QR connect + rotation/warm-up | ✔ | accounts grid, QR modal | account status cards |
| M5 | Proxies per account | ✔ | proxy manager | — (read in account detail) |
| M6 | Contact groups, import CSV/Excel, validation, tags | ✔ | groups, import wizard, validator | group/contact viewer + tags |
| M7 | Group scraper (via bridge) | ✔ | extract dialog | viewer |
| M8 | Templates + rich media + interactive buttons/lists | ✔ | template builder | preview |
| M9 | Campaign builder, spintax, personalization, scheduler, progress | ✔ | single-screen launch, progress bar, schedule | campaign list + live progress |
| M10 | Anti-ban: pacing, batch pause, rotation, daily caps | ✔ | pacing controls | read-only settings |
| M11 | Auto-reply rules (exact/wildcard/regex) | ✔ | rules table | toggle bot on/off |
| M12 | AI chatbot config (OpenAI/Anthropic) | ✔ | provider form + test | — |
| M13 | Unified inbox (threads, statuses, manual reply) | ✔ | inbox split view | chat UI two-way |
| M14 | Reports/delivery stats, CSV/Excel export | ✔ | reports charts + export | analytics tiles |
| M15 | Dashboard (KPIs from §8) | ✔ | dashboard | dashboard |
| M16 | Webhooks (Meta) + bridge callbacks | ✔ | — | — |
| M17 | Settings, audit log, health | ✔ | settings/audit | settings |
| M18 | FCM push tokens | ✔ | — | registration + local alerts |

---

## 6. API surface (summary — full detail in `API_CONTRACT.md`)

* `POST /api/auth/login`, `POST /api/auth/refresh`, `GET /api/auth/me`, `POST /api/auth/logout`
* `GET|POST /api/users`, `GET|PUT|DELETE /api/users/{id}`, `GET /api/users/tree`
* `GET /api/licenses`, `POST /api/licenses` (hierarchy-checked), `POST /api/licenses/{key}/revoke`
* `POST /api/licenses/activate` *(public, HWID)*, `POST /api/licenses/verify` *(offline-friendly)*, `GET /api/licenses/{id}/activations`, `DELETE /api/activations/{id}`
* `GET|POST|PUT|DELETE /api/whatsapp-accounts`, `POST /api/whatsapp-accounts/{id}/connect`, `GET .../qr`, `POST .../rotate`, `GET .../status`
* `GET|POST|PUT|DELETE /api/proxies`, `POST /api/proxies/{id}/test`
* `GET|POST /api/contact-groups`, `POST /api/contact-groups/{id}/import`, `POST /api/contact-groups/validate`, `GET /api/contact-groups/{id}/export`, `POST /api/contact-groups/scrape`
* `GET|POST|PUT|DELETE /api/contacts` (+ `tags` endpoints)
* `GET|POST|PUT|DELETE /api/templates`
* `GET|POST|PUT|DELETE /api/campaigns`, `POST /api/campaigns/{id}/schedule|start|pause|resume|stop`, `GET /api/campaigns/{id}/progress`, `GET /api/campaigns/{id}/report?format=csv|xlsx`, `GET /api/campaigns/{id}/recipients`
* `GET|PUT /api/anti-ban/{accountId}`
* `GET|POST|PUT|DELETE /api/auto-reply-rules`
* `GET|PUT /api/ai-settings`, `POST /api/ai-settings/test`
* `GET /api/inbox/threads`, `GET /api/inbox/threads/{id}/messages`, `POST /api/inbox/threads/{id}/messages`, `PUT /api/inbox/threads/{id}/bot`
* `GET /api/reports/delivery`, `GET /api/reports/kpis`
* `GET /api/dashboard/stats`
* `POST /api/webhooks/meta` *(public)*, `POST /api/bridge/events` *(service key)*
* `POST /api/push-tokens`, `DELETE /api/push-tokens/{token}`
* `GET /api/settings`, `PUT /api/settings`, `GET /api/audit-logs`, `GET /healthz`

---

## 7. Milestones & delivery order

| Milestone | Deliverable | Exit criteria |
|---|---|---|
| **M0 — Plan & contract** | `docs/PLAN.md`, `docs/API_CONTRACT.md`, repo skeleton | plan reviewed |
| **M1 — Backend foundation** | solution, entities, DbContext, migrations, JWT+RBAC, Swagger, seed (roles + super admin), health | `dotnet build` + `dotnet test` green; Swagger lists auth |
| **M2 — Messaging modules** | accounts, proxies, contacts/groups/import/validation, templates, campaigns + worker (pacing, rotation, resume), anti-ban, inbox, rules, AI settings, reports | campaign worker unit tests pass incl. resume-no-duplicate test |
| **M3 — Licensing & hierarchy** | key generation (signed), activation/verify/revoke, hierarchy enforcement, audit log | license lifecycle tests pass |
| **M4 — Angular web** | shell, nav, auth guard, all 18 module screens wired to contract | `ng build` succeeds; login → dashboard → campaign flow works |
| **M5 — Flutter app** | splash/login, dashboard, campaigns, inbox, contacts, licenses, settings, FCM stub | `flutter analyze` clean; app runs on chrome/emulator |
| **M6 — Bridge service** | Node/Baileys QR + send + status callback, service key auth | local smoke: QR session, send, callback recorded |
| **M7 — Hardening** | rate limiting, input validation (FluentValidation), pagination, masking, README, docker-compose (postgres + api + web) | docs + compose reviewed |

Sequencing note: M1→M2→M3 are the critical path; M4/M5/M6 run against the frozen contract in parallel with M2/M3.

---

## 8. Quality gates

* `dotnet build` / `dotnet test` (xUnit: licensing, spintax, personalization, pacing math, resume-no-duplicate, RBAC, validation).
* `ng build` (production config) must pass; Angular unit tests for services/guards.
* `flutter analyze` must be clean; widget test for login + campaign list.
* API contract is the single source of truth — web/mobile generated from `API_CONTRACT.md`, verified with Swagger at integration time.
* Security: JWT access (15 min) + rotating refresh tokens; BCrypt/Argon2 password hashes; secrets in config/env, never in logs; proxy & AI & Meta tokens encrypted at rest (`IDataProtector`); phone numbers masked in audit/log output.

---

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Meta policy enforcement / bans on unofficial Baileys automation | dual-engine model; official Cloud API path for high volume; strict pacing/rotation defaults on Baileys |
| WhatsApp web protocol changes break Baileys | bridge is a separate service, updatable independently; contract isolated behind `IWhatsAppChannel` |
| No PostgreSQL server on this dev machine | provider switch (Postgres default / Sqlite dev), migrations generated against model, docker-compose provided for real runs |
| Contact data leakage | encryption at rest, masked logging, no third-party telemetry |
| Duplicate sends after crash | unique `(campaignId, phone)` + checkpoint resume, tested |

---

## 10. Immediate next steps (this session)

1. ✔ Plan + contract authored.
2. Scaffold `backend/` solution, entities, migrations, JWT/RBAC, Swagger.
3. Implement all API modules (M2, M3).
4. Scaffold `web/` Angular app with every screen.
5. Scaffold `mobile/` Flutter app with every screen.
6. Node `services/wa-bridge` skeleton.
7. Build/test everything; report status.
