# Custom Domain Setup — winmaster.wintechinov.com (Cloudflare + Render)

Serve the app at **https://winmaster.wintechinov.com** (same Render service, DB, and container).

The app code is already prepared:
- `render.yaml` declares the domain.
- The API CORS allowlist includes the subdomain.
- The Angular web app is served same-origin (works on any hostname automatically).
- The Flutter app defaults its API base URL to `https://winmaster.wintechinov.com`.

Two steps remain — both in your accounts (Render + Cloudflare).

---

## Step 1 — Render: add the custom domain

1. Render dashboard → **win-master** service → **Settings → Custom Domains**.
2. **Add Custom Domain** → `winmaster.wintechinov.com` → **Save**.
3. Render shows a **CNAME target** (looks like `win-master.onrender.com`). Copy the exact value.

> If the blueprint already added it from `render.yaml`, it will appear here as "Verifying".

---

## Step 2 — Cloudflare: add the DNS record

In Cloudflare dashboard → select the **wintechinov.com** zone → **DNS → Records → Add record**:

| Field | Value |
|---|---|
| **Type** | `CNAME` |
| **Name** | `winmaster` |
| **Target** | `win-master.onrender.com`  *(use the exact target Render shows in Step 1)* |
| **Proxy status** | **DNS only (grey cloud)** — see note below |
| **TTL** | Auto |

**Important — Proxy status:**
- Set it to **DNS only (grey cloud)** first. This lets Render verify the domain and issue its free Let's Encrypt SSL certificate.
- After Render shows the domain as **Verified** with a valid certificate (usually a few minutes), you *may* switch it to **Proxied (orange cloud)** to use Cloudflare's CDN/WAF. If you proxy immediately, keep Cloudflare SSL/TLS mode on **Full (strict)** so it doesn't conflict with Render's cert.

---

## Step 3 — Verify

- In Render, the domain flips **Verifying → Verified** once the CNAME resolves (minutes to ~1 hour).
- Render auto-issues HTTPS, so **https://winmaster.wintechinov.com** will load the app with a valid certificate.
- Log in with the demo accounts (password `Win@12345`): `admin@winmaster.io` · `reseller@winmaster.io` · `user@winmaster.io`.

---

## (Optional) Create the DNS record via Cloudflare API

If you'd rather script it, create a token (My Profile → API Tokens → **Edit zone DNS** on `wintechinov.com`) and run:

```bash
curl -X POST "https://api.cloudflare.com/client/v4/zones/<ZONE_ID>/dns_records" \
  -H "Authorization: Bearer <CF_API_TOKEN>" \
  -H "Content-Type: application/json" \
  --data '{
    "type": "CNAME",
    "name": "winmaster",
    "content": "win-master.onrender.com",
    "ttl": 1,
    "proxied": false
  }'
```

Replace `<ZONE_ID>` (Cloudflare → wintechinov.com → Overview → Zone ID) and `<CF_API_TOKEN>`,
and use the exact Render target for `content`.

> Share a Cloudflare API token (and I can look up the zone ID) if you want me to create the record for you.
