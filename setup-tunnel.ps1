# One-time setup for the Cloudflare Tunnel that exposes your LOCAL Win Master
# (http://localhost:5249) to the internet at a hostname on your domain, so your PC
# can serve the app publicly when it's on. No port-forwarding or public IP needed.
#
# Run this ONCE. It will: download cloudflared, log you in to Cloudflare (browser),
# create a tunnel, and route a hostname to your local app.
#
#   .\setup-tunnel.ps1
#
# After setup, start the tunnel any time with:   .\run-tunnel.ps1
# (and run Win Master locally with start-local.cmd)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$bin = Join-Path $root "tools"
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$cf = Join-Path $bin "cloudflared.exe"

# The public hostname your PC will serve on. Pick a sub-name so it can fail over to Render.
$HOSTNAME = "app.wintechinov.com"   # change if you prefer another sub-domain
$TUNNEL   = "winmaster-local"

# 1) Download cloudflared (Windows amd64) if we don't have it.
if (-not (Test-Path $cf)) {
  Write-Host "Downloading cloudflared..." -ForegroundColor Cyan
  $url = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"
  Invoke-WebRequest -Uri $url -OutFile $cf -UseBasicParsing
}
Write-Host ("cloudflared: " + (& $cf --version)) -ForegroundColor Green

# 2) Log in to Cloudflare (opens a browser — pick the wintechinov.com zone).
Write-Host "`nA browser will open — log in and authorize the 'wintechinov.com' zone." -ForegroundColor Yellow
& $cf tunnel login

# 3) Create the tunnel (idempotent — ignore 'already exists').
Write-Host "`nCreating tunnel '$TUNNEL'..." -ForegroundColor Cyan
& $cf tunnel create $TUNNEL 2>$null | Out-Host

# 4) Route the hostname to this tunnel (creates/updates the DNS CNAME in Cloudflare).
Write-Host "Routing https://$HOSTNAME to this PC..." -ForegroundColor Cyan
& $cf tunnel route dns $TUNNEL $HOSTNAME

# 5) Write a config that forwards the tunnel to the local Win Master API.
$cfgDir = Join-Path $env:USERPROFILE ".cloudflared"
$config = @"
tunnel: $TUNNEL
credentials-file: $cfgDir\$TUNNEL.json
ingress:
  - hostname: $HOSTNAME
    service: http://localhost:5249
  - service: http_status:404
"@
# Find the actual tunnel UUID credentials file to reference precisely.
$cred = Get-ChildItem $cfgDir -Filter "*.json" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($cred) { $config = $config -replace [regex]::Escape("$cfgDir\$TUNNEL.json"), $cred.FullName }
Set-Content -Path (Join-Path $root "cloudflared-config.yml") -Value $config -Encoding ASCII

Write-Host "`nSetup complete!" -ForegroundColor Green
Write-Host "Your PC will serve Win Master at:  https://$HOSTNAME" -ForegroundColor Green
Write-Host "Next: run  start-local.cmd  (the app) and  .\run-tunnel.ps1  (the tunnel)." -ForegroundColor Yellow
