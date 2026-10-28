# Win Master

WhatsApp automation, bulk messaging, mini-CRM & multi-tier licensing platform.

**Stack:** ASP.NET Core 9 Web API (controllers, EF Core, JWT) · Angular 18 web · Flutter companion app · PostgreSQL.

## One-click deploy to Render

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/navin4489-ai/win-master)

This provisions a Docker web service (API serving the Angular SPA) + a managed PostgreSQL database from `render.yaml`.
First boot runs migrations and seeds demo data.

**Demo logins** (password `Win@12345`): `admin@winmaster.io` (SuperAdmin) · `reseller@winmaster.io` · `user@winmaster.io`

## Local development

```bash
# API (uses SQLite locally, Postgres in production)
cd backend/WinMaster.API
dotnet run            # http://localhost:5188  (Swagger at /swagger)

# Web
cd web/winmaster-web
npm install
npm start             # http://localhost:4200
```

## Repository layout

```
Dockerfile          multi-stage build (Angular -> .NET -> runtime, one container)
render.yaml         Render blueprint: web service + PostgreSQL
backend/            WinMaster.sln (API, Core, Infrastructure, Tests)
web/winmaster-web/  Angular 18 app (all modules)
docs/               PLAN.md, API_CONTRACT.md, requirements
```

See `docs/PLAN.md` and `docs/API_CONTRACT.md` for the full design and API.
