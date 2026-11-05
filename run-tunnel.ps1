# Starts the Cloudflare Tunnel so your PC serves Win Master publicly (run after setup-tunnel.ps1).
# Keep this window open while you want your PC to be the live host.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$cf = Join-Path $root "tools\cloudflared.exe"
$cfg = Join-Path $root "cloudflared-config.yml"
if (-not (Test-Path $cf))  { Write-Host "Run setup-tunnel.ps1 first." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $cfg)) { Write-Host "Run setup-tunnel.ps1 first." -ForegroundColor Red; exit 1 }
Write-Host "Starting Cloudflare Tunnel (serving http://localhost:5249 publicly)..." -ForegroundColor Green
& $cf tunnel --config $cfg run
