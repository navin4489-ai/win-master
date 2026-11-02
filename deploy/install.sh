#!/usr/bin/env bash
# One-command installer for Win Master on a fresh Ubuntu VM (Oracle Cloud Always-Free etc.).
# Installs Docker, fetches the code, generates secrets on first run, and starts the stack.
#
# Usage on the VM:
#   # Plain HTTP (access via http://<vm-ip>/):
#   curl -fsSL https://raw.githubusercontent.com/navin4489-ai/win-master/main/deploy/install.sh | bash
#
#   # With HTTPS for a domain (A record must already point here, ports 80+443 open):
#   curl -fsSL https://raw.githubusercontent.com/navin4489-ai/win-master/main/deploy/install.sh | DOMAIN=winmaster.yourdomain.com bash
#
set -euo pipefail

REPO_URL="https://github.com/navin4489-ai/win-master.git"
APP_DIR="${APP_DIR:-$HOME/win-master}"
DOMAIN="${DOMAIN:-}"

log() { echo -e "\033[1;32m[win-master]\033[0m $*"; }

# --- 1. Docker ---
if ! command -v docker >/dev/null 2>&1; then
  log "Installing Docker..."
  sudo apt-get update -y
  sudo apt-get install -y ca-certificates curl git openssl
  sudo install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
  sudo chmod a+r /etc/apt/keyrings/docker.gpg
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
  sudo apt-get update -y
  sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
  sudo usermod -aG docker "$USER" || true
else
  log "Docker already installed."
  sudo apt-get install -y git openssl >/dev/null 2>&1 || true
fi

# Run docker via sudo if the current shell isn't in the docker group yet.
DC="docker"
if ! docker ps >/dev/null 2>&1; then DC="sudo docker"; fi

# --- 2. Code ---
if [ -d "$APP_DIR/.git" ]; then
  log "Updating existing checkout in $APP_DIR..."
  git -C "$APP_DIR" pull --ff-only
else
  log "Cloning repo into $APP_DIR..."
  git clone "$REPO_URL" "$APP_DIR"
fi
cd "$APP_DIR"

# --- 3. Secrets (.env) — generated once, preserved on re-runs ---
ENV_FILE="deploy/.env"
if [ ! -f "$ENV_FILE" ]; then
  log "Generating deploy/.env with fresh random secrets..."
  gen() { openssl rand -base64 48 | tr -d '\n/+=' | cut -c1-40; }
  {
    echo "POSTGRES_USER=winmaster"
    echo "POSTGRES_PASSWORD=$(gen)"
    echo "POSTGRES_DB=winmaster"
    echo "JWT_SECRET=$(gen)$(gen)"
    echo "LICENSE_SIGNING_SECRET=$(gen)"
    echo "BRIDGE_KEY=$(gen)"
    echo "DOMAIN=${DOMAIN}"
  } > "$ENV_FILE"
  chmod 600 "$ENV_FILE"
else
  log "deploy/.env already exists — keeping your secrets."
  # If a domain was passed and .env has an empty DOMAIN, fill it in.
  if [ -n "$DOMAIN" ] && grep -q '^DOMAIN=$' "$ENV_FILE"; then
    sed -i "s|^DOMAIN=$|DOMAIN=${DOMAIN}|" "$ENV_FILE"
  fi
fi

# --- 4. Start ---
if [ -n "$DOMAIN" ]; then
  log "Starting stack WITH HTTPS for ${DOMAIN}..."
  $DC compose -f deploy/docker-compose.https.yml --env-file "$ENV_FILE" up -d --build
  log "Done. Once DNS + ports 80/443 are ready, open: https://${DOMAIN}/"
else
  log "Starting stack on HTTP (port 80)..."
  $DC compose -f deploy/docker-compose.yml --env-file "$ENV_FILE" up -d --build
  IP="$(curl -fsSL https://api.ipify.org 2>/dev/null || echo '<vm-ip>')"
  log "Done. Open: http://${IP}/"
fi

log "Login: admin@winmaster.io / Win@12345  (change the password after first login)."
log "Logs:  $DC compose -f deploy/docker-compose.yml logs -f app"
