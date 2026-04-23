# Publish TradeTracker: build frontend, copy to wwwroot, publish API to publish-out
# Default mode prepares a manifest of only files whose SOURCE changed today.
# Run from repo root: .\publish.ps1
# Optional full manifest: .\publish.ps1 -AllFiles

param(
    [switch]$AllFiles
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$apiRoot = Join-Path $root "TradeHelperAPI"
$frontendRoot = Join-Path $root "TradeTrackerFrontEnd"
$publishOut = Join-Path $apiRoot "publish-out"
$includeManifestPath = Join-Path $publishOut ".deploy-include.json"
$todayStart = (Get-Date).Date

function Get-RepoFilesChangedToday {
    param(
        [string]$BasePath,
        [string[]]$ExcludePathPatterns
    )

    return @(Get-ChildItem -Path $BasePath -Recurse -File | Where-Object {
        if ($_.LastWriteTime -lt $todayStart) { return $false }

        foreach ($pattern in $ExcludePathPatterns) {
            if ($_.FullName -like $pattern) { return $false }
        }
        return $true
    })
}

function Add-PublishFile {
    param(
        [System.Collections.Generic.HashSet[string]]$IncludeSet,
        [string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) { return }
    $normalized = $RelativePath.Replace("\", "/").TrimStart("/")
    $targetPath = Join-Path $publishOut ($normalized.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
    if (Test-Path $targetPath) {
        [void]$IncludeSet.Add($normalized)
    }
}

function Add-PublishFilesByPattern {
    param(
        [System.Collections.Generic.HashSet[string]]$IncludeSet,
        [string]$Pattern
    )

    $matches = Get-ChildItem -Path $publishOut -File | Where-Object { $_.Name -like $Pattern }
    foreach ($file in $matches) {
        [void]$IncludeSet.Add($file.Name)
    }
}

Write-Host "1. Building frontend (Vite)..." -ForegroundColor Cyan
Set-Location $frontendRoot
npx vite build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n2. Copying dist to wwwroot..." -ForegroundColor Cyan
Copy-Item -Path "dist\*" -Destination "..\TradeHelperAPI\wwwroot\" -Recurse -Force

Write-Host "`n3. Publishing API to publish-out..." -ForegroundColor Cyan
Set-Location $apiRoot
dotnet publish TradeHelperAPI.csproj -c Release -o publish-out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$includeSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

if ($AllFiles) {
    Write-Host "`n4. Full deploy manifest requested..." -ForegroundColor Yellow
    Get-ChildItem -Path $publishOut -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($publishOut.Length).TrimStart("\", "/").Replace("\", "/")
        if ($rel -notmatch "\.(user|pubxml)$" -and $rel -ne ".deploy-include.json") {
            [void]$includeSet.Add($rel)
        }
    }
} else {
    Write-Host "`n4. Building today-only deploy manifest..." -ForegroundColor Cyan

    $frontendChangedToday = Get-RepoFilesChangedToday -BasePath $frontendRoot -ExcludePathPatterns @(
        (Join-Path $frontendRoot "node_modules\*"),
        (Join-Path $frontendRoot "dist\*")
    )

    $apiChangedToday = Get-RepoFilesChangedToday -BasePath $apiRoot -ExcludePathPatterns @(
        (Join-Path $apiRoot "bin\*"),
        (Join-Path $apiRoot "obj\*"),
        (Join-Path $apiRoot "publish-out\*"),
        (Join-Path $apiRoot "publish-test\*")
    )

    if ($frontendChangedToday.Count -gt 0) {
        Write-Host "Frontend files changed today: $($frontendChangedToday.Count). Including published wwwroot assets." -ForegroundColor Cyan
        Get-ChildItem -Path (Join-Path $publishOut "wwwroot") -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
            $rel = $_.FullName.Substring($publishOut.Length).TrimStart("\", "/").Replace("\", "/")
            [void]$includeSet.Add($rel)
        }
    }

    $backendBinaryRefresh = $false
    foreach ($file in $apiChangedToday) {
        $rel = $file.FullName.Substring($apiRoot.Length).TrimStart("\", "/")

        if ($rel -like "wwwroot\*") {
            Add-PublishFile -IncludeSet $includeSet -RelativePath $rel
            continue
        }

        if ($rel -like "appsettings*.json" -or $rel -eq "web.config" -or $rel -eq "logs\.gitkeep") {
            Add-PublishFile -IncludeSet $includeSet -RelativePath $rel
            continue
        }

        if ($rel -like "Properties\PublishProfiles\*" -or $rel -like "*.pubxml" -or $rel -like "*.pubxml.user") {
            continue
        }

        $backendBinaryRefresh = $true
    }

    if ($backendBinaryRefresh) {
        Write-Host "Backend source/config changed today. Including current app runtime payload." -ForegroundColor Cyan
        Add-PublishFilesByPattern -IncludeSet $includeSet -Pattern "TradeHelperAPI*"
        Add-PublishFilesByPattern -IncludeSet $includeSet -Pattern "appsettings*.json"
        Add-PublishFile -IncludeSet $includeSet -RelativePath "web.config"
        Add-PublishFile -IncludeSet $includeSet -RelativePath "logs/.gitkeep"
    }
}

$includeList = @($includeSet | Sort-Object)
$includeList | ConvertTo-Json | Set-Content $includeManifestPath -Encoding UTF8

Set-Location $root
Write-Host "`nDone. Output: $publishOut" -ForegroundColor Green
Write-Host "Deploy manifest: $includeManifestPath" -ForegroundColor Green
Write-Host "Files selected for deploy: $($includeList.Count)" -ForegroundColor Green
