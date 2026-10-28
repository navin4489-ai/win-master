#!/usr/bin/env bash
set -e

# Co-hosted startup for Render's free tier: run the Node/Baileys WhatsApp bridge
# alongside the .NET API in a single container. The API talks to the bridge over
# localhost (Bridge__BaseUrl), and the bridge posts callbacks back to the API.

# The API binds to Render's injected $PORT (default 10000 locally).
API_PORT="${PORT:-10000}"

# Share the bridge secret: the API reads Bridge__Key, the bridge reads BRIDGE_KEY.
export BRIDGE_KEY="${Bridge__Key:-${BRIDGE_KEY:-dev-bridge-key}}"
# The bridge posts events back to the API on the same container.
export API_BASE_URL="${API_BASE_URL:-http://localhost:${API_PORT}}"
export BRIDGE_PORT="${BRIDGE_PORT:-3001}"
export SESSIONS_DIR="${SESSIONS_DIR:-/app/bridge/sessions}"
export NODE_ENV=production

mkdir -p "$SESSIONS_DIR"

echo "[entrypoint] starting WhatsApp bridge on :$BRIDGE_PORT (callbacks -> $API_BASE_URL, sessions: $SESSIONS_DIR)"
# Unset PORT for the bridge so Render's API port never hijacks it.
( unset PORT; exec node /app/bridge/src/server.js ) &
BRIDGE_PID=$!

# If the bridge dies, keep the API running (QR connect will report unavailable).
trap 'kill $BRIDGE_PID 2>/dev/null || true' EXIT

echo "[entrypoint] starting .NET API on :$API_PORT"
exec dotnet WinMaster.API.dll
