# Run Win Master locally on this PC (no Docker) — API + Angular web + WhatsApp bridge.
# Requires: .NET 9 SDK and Node.js (already installed on this machine).
#
# The API connects to the SHARED Render Postgres database (same data as the Render site —
# no separate local DB, no sync drift). The WhatsApp session is stored on this PC and stays
# connected while this window is open, so campaign sending works here.
#
#   Start:  double-click start-local.cmd   (or run  .\run-local.ps1  from this folder)
#   Open:   http://localhost:5249/         (login: admin@winmaster.io / Win@12345)
#   Stop:   press Ctrl+C in this window.
#
# NOTE: Render's free Postgres expires ~30 days after creation. When that happens, create a
# new DB on Render and paste its External Database URL into $DatabaseUrl below.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$apiPort = 5249
$bridgePort = 3001
$bridgeKey = "dev-bridge-key"

# Shared Render Postgres (External Database URL). Env var DATABASE_URL overrides this if set.
$DatabaseUrl = if ($env:DATABASE_URL) { $env:DATABASE_URL } else {
  "postgresql://winmaster_user:ONHS3ubTARWpTsUiT5NFDynWru0TlnxL@dpg-dbgrcsnf3r2c73ag5mi0-a.oregon-postgres.render.com/winmaster"
}

Write-Host "`n=== Win Master - local run ===" -ForegroundColor Green

# 1) Ensure the Angular web is built and served by the API (copied into wwwroot).
$webDist = Join-Path $root "web\winmaster-web\dist\winmaster-web\browser"
if (-not (Test-Path (Join-Path $webDist "index.html"))) {
  Write-Host "[1/3] Building web UI (first run only, ~1 min)..." -ForegroundColor Cyan
  Push-Location (Join-Path $root "web\winmaster-web")
  if (-not (Test-Path node_modules)) { npm install }
  & .\node_modules\.bin\ng.cmd build --configuration production
  Pop-Location
} else {
  Write-Host "[1/3] Web UI already built." -ForegroundColor Cyan
}
$wwwroot = Join-Path $root "backend\WinMaster.API\wwwroot"
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $webDist "*") -Destination $wwwroot -Recurse -Force

# 2) Start the WhatsApp bridge (Node) in its own window so you can see QR/logs.
Write-Host "[2/3] Starting WhatsApp bridge on :$bridgePort ..." -ForegroundColor Cyan
$bridgeDir = Join-Path $root "services\wa-bridge"
if (-not (Test-Path (Join-Path $bridgeDir "node_modules"))) {
  Push-Location $bridgeDir; $env:PUPPETEER_SKIP_DOWNLOAD = "true"; npm install; Pop-Location
}
$bridgeEnv = @(
  "set BRIDGE_PORT=$bridgePort",
  "set API_BASE_URL=http://localhost:$apiPort",
  "set BRIDGE_KEY=$bridgeKey",
  "set SESSIONS_DIR=C:\winmaster-data\sessions",
  "set PUPPETEER_SKIP_DOWNLOAD=true",
  "node src\server.js"
) -join " && "
$bridge = Start-Process -FilePath "cmd.exe" -ArgumentList "/k", "cd /d `"$bridgeDir`" && $bridgeEnv" -PassThru

# 3) Start the .NET API (serves web UI + talks to the bridge). Blocks until Ctrl+C.
Write-Host "[3/3] Starting Win Master API on http://localhost:$apiPort ..." -ForegroundColor Cyan
Write-Host "`n  -> Open  http://localhost:$apiPort/  and log in (admin@winmaster.io / Win@12345)`n" -ForegroundColor Yellow
Push-Location (Join-Path $root "backend\WinMaster.API")
# IMPORTANT: Production env (not Development) so the app uses the Postgres provider, NOT the
# SQLite in appsettings.Development.json. This points the LOCAL app at the SHARED Render DB.
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://localhost:$apiPort"
$env:Bridge__Key = $bridgeKey
$env:Bridge__BaseUrl = "http://localhost:$bridgePort"
# Use the shared Render Postgres (one database for both local and Render — always in sync).
$env:Database__Provider = "Postgres"
$env:DATABASE_URL = $DatabaseUrl
# Sessions dir (WhatsApp creds) still lives on this PC.
New-Item -ItemType Directory -Force -Path "C:\winmaster-data" | Out-Null
try {
  dotnet run --no-launch-profile
} finally {
  if ($bridge -and -not $bridge.HasExited) { Stop-Process -Id $bridge.Id -Force -ErrorAction SilentlyContinue }
  Pop-Location
  Write-Host "`nWin Master stopped." -ForegroundColor Green
}
