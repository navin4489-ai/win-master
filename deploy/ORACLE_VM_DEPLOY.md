# Deploy Win Master on an always-on free VM (Oracle Cloud Always-Free)

This runs the whole app (API + Angular web + Baileys WhatsApp bridge) and Postgres on
one free Linux VM that **never sleeps**, with persistent storage — so the WhatsApp
session and all data survive reboots. This is the free way to keep WhatsApp linked 24/7.

> You keep the existing Render deployment too; this is an additional, more reliable home.

---

## 1. Create the free VM (one time)

1. Sign up at https://www.oracle.com/cloud/free/ (a card is required for identity
   verification only; the **Always Free** resources are never charged).
2. In the console: **Menu → Compute → Instances → Create instance**.
   - **Image:** Ubuntu 22.04
   - **Shape:** `VM.Standard.A1.Flex` (Ampere/ARM, Always Free) — give it **2 OCPU / 12 GB RAM**
     (well within the free allowance and plenty for this stack). If ARM capacity is
     unavailable in your region, use `VM.Standard.E2.1.Micro` (x86 Always Free).
   - **SSH keys:** upload your public key (or let it generate one and download it).
3. After it boots, note the **public IP** (e.g. `140.x.x.x`).

### Open the firewall (two places)
- **Oracle "Security List" (cloud firewall):** VCN → Security Lists → default →
  add an **Ingress rule**: Source `0.0.0.0/0`, protocol TCP, destination port **80**
  (and **443** later if you add HTTPS).
- **VM's own firewall (Ubuntu):** run on the VM:
  ```bash
  sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
  sudo netfilter-persistent save
  ```

---

## 2. Install Docker on the VM

SSH in (`ssh ubuntu@<public-ip>`), then:
```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl git
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
sudo usermod -aG docker ubuntu
newgrp docker   # or log out and back in
```

---

## 3. Get the code and configure secrets

```bash
git clone https://github.com/navin4489-ai/win-master.git
cd win-master
cp deploy/.env.example deploy/.env
nano deploy/.env        # set strong random values (see below)
```

Generate secrets on the VM and paste them in:
```bash
openssl rand -base64 48   # run 4 times → POSTGRES_PASSWORD, JWT_SECRET, LICENSE_SIGNING_SECRET, BRIDGE_KEY
```

---

## 4. Build and start

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
```
First build takes a few minutes. Check it's up:
```bash
docker compose -f deploy/docker-compose.yml ps
docker compose -f deploy/docker-compose.yml logs -f app   # Ctrl+C to stop following
```

Open `http://<public-ip>/` in a browser → the Win Master login loads.
Log in with the seeded admin (`admin@winmaster.io` / `Win@12345`) and **change the password**.

---

## 5. Point your domain (optional)

To use `winmaster.wintechinov.com` (or any domain) on this VM instead of/in addition to Render:
- In Cloudflare DNS, set an **A record**: `winmaster` → `<public-ip>` (DNS-only, grey cloud, to start).
- The app serves on port 80. For HTTPS, add Caddy/Traefik later, or enable Cloudflare's proxy
  (orange cloud) with "Flexible" SSL as a quick start.

---

## 6. Day-2 operations

- **Update to the latest code:**
  ```bash
  cd ~/win-master && git pull
  docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
  ```
- **Restart:** `docker compose -f deploy/docker-compose.yml restart app`
- **Logs:** `docker compose -f deploy/docker-compose.yml logs --tail=200 app`
- **Backup the database:**
  ```bash
  docker compose -f deploy/docker-compose.yml exec db pg_dump -U winmaster winmaster > backup_$(date +%F).sql
  ```

### Why this keeps WhatsApp connected
The VM never sleeps and the `sessions` + `pgdata` Docker volumes persist across reboots,
so a scanned WhatsApp link stays valid. If the app container restarts, the bridge
restores the session from the persisted credentials automatically — no re-scan needed.
