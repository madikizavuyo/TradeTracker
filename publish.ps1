# Publish TradeTracker: build frontend, copy to wwwroot, publish API to publish-out
# Run from repo root: .\publish.ps1

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "1. Building frontend (Vite)..." -ForegroundColor Cyan
Set-Location "$root\TradeTrackerFrontEnd"
npx vite build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n2. Copying dist to wwwroot..." -ForegroundColor Cyan
Copy-Item -Path "dist\*" -Destination "..\TradeHelperAPI\wwwroot\" -Recurse -Force

Write-Host "`n3. Publishing API to publish-out..." -ForegroundColor Cyan
Set-Location "$root\TradeHelperAPI"
dotnet publish TradeHelperAPI.csproj -c Release -o publish-out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Set-Location $root
Write-Host "`nDone. Output: $root\TradeHelperAPI\publish-out\" -ForegroundColor Green
