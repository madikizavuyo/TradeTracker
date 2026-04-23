# FTP deploy to SmarterASP - overwrites files (no delete)
# By default only uploads changed files (incremental).
# If publish.ps1 created .deploy-include.json, only those listed files are considered for upload.
# Use -ForceFull for full deploy.
# Use -ModifiedWithinHours 4 to upload only files whose LastWriteTimeUtc is within the last N hours (ignores deploy-include manifest).
# IMPORTANT: Stop the site in SmarterASP Control Panel first to avoid 550 errors on locked files
param(
    [string]$FtpHost = "win6053.site4now.net",
    [string]$FtpUser = "veeg2fresh-001",
    [string]$FtpPass,
    [string]$RemotePath = "/site1",
    [string]$SourcePath = "",
    [switch]$ForceFull,
    [ValidateRange(0, 168)]
    [int]$ModifiedWithinHours = 0
)

if (-not $FtpPass) {
    $userFile = Join-Path $PSScriptRoot "Properties\PublishProfiles\veeg2fresh-FTP.pubxml.user"
    if (Test-Path $userFile) {
        [xml]$xml = Get-Content $userFile
        $FtpPass = $xml.Project.PropertyGroup.Password
    }
    if (-not $FtpPass) { throw "FTP password required. Set -FtpPass or add to .pubxml.user" }
}

if (-not $SourcePath) { $SourcePath = Join-Path $PSScriptRoot "publish-out" }
$SourcePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SourcePath)
if (-not (Test-Path $SourcePath)) { throw "Source path not found: $SourcePath" }

$includeManifestPath = Join-Path $SourcePath ".deploy-include.json"
$includeSet = $null
if (-not $ForceFull -and $ModifiedWithinHours -eq 0 -and (Test-Path $includeManifestPath)) {
    try {
        $includeRaw = Get-Content $includeManifestPath -Raw | ConvertFrom-Json
        $includeSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($item in @($includeRaw)) {
            if (-not [string]::IsNullOrWhiteSpace($item)) {
                [void]$includeSet.Add(($item -replace "\\", "/").TrimStart("/"))
            }
        }
        Write-Host "Deploy include manifest: $includeManifestPath ($($includeSet.Count) file(s))" -ForegroundColor Cyan
    } catch {
        Write-Host "WARNING: Failed to read deploy include manifest $includeManifestPath. Falling back to normal file discovery." -ForegroundColor Yellow
        $includeSet = $null
    }
}

function Write-FtpFile {
    param([string]$LocalPath, [string]$RemotePath)
    $uri = "ftp://$FtpHost$RemotePath"
    $req = [System.Net.FtpWebRequest]::Create($uri)
    $req.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $req.Credentials = New-Object System.Net.NetworkCredential($FtpUser, $FtpPass)
    $req.UseBinary = $true
    $req.UsePassive = $true
    $req.KeepAlive = $false
    $fs = [System.IO.File]::OpenRead($LocalPath)
    $req.ContentLength = $fs.Length
    try {
        $rs = $req.GetRequestStream()
        $fs.CopyTo($rs)
        $rs.Close()
    } finally { $fs.Close() }
    $resp = $req.GetResponse()
    $resp.Close()
}

function Ensure-FtpDir {
    param([string]$RemotePath)
    $uri = "ftp://$FtpHost$RemotePath"
    $req = [System.Net.FtpWebRequest]::Create($uri)
    $req.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
    $req.Credentials = New-Object System.Net.NetworkCredential($FtpUser, $FtpPass)
    $req.UsePassive = $true
    $req.KeepAlive = $false
    try { $req.GetResponse() | Out-Null } catch { }
}

Write-Host "Source: $SourcePath" -ForegroundColor Cyan
Write-Host "Target: ftp://$FtpHost$RemotePath" -ForegroundColor Cyan
Write-Host "*** Stop the site in SmarterASP Control Panel first to avoid 550 errors on locked files. ***" -ForegroundColor Yellow

# Ensure we're in API project dir
$apiDir = Split-Path $SourcePath -Parent
if ($apiDir -eq $SourcePath) { $apiDir = $PSScriptRoot }
Set-Location $apiDir

# Upload files recursively
# - Skip wwwroot/Identity (locked when site runs)
# - Skip nested publish-test, publish-out junk from bad publishes
$allFiles = @(Get-ChildItem -Path $SourcePath -Recurse -File)
$files = @($allFiles | Where-Object {
    $rel = $_.FullName.Substring($SourcePath.Length).TrimStart("\", "/").Replace("/", "\")
    $rel -notmatch "wwwroot\\Identity" -and
    $rel -notmatch "^publish-test\\" -and
    $rel -notmatch "^publish-out\\" -and
    $rel -notmatch "\\publish-test\\" -and
    $rel -notmatch "\\publish-out\\" -and
    $rel -notmatch "\\publish\\publish\\" -and
    $rel -ne ".deploy-include.json"
})
$identityFiles = $allFiles | Where-Object { $_.FullName -match [regex]::Escape("wwwroot\Identity\") }
if ($identityFiles.Count -gt 0) {
    Write-Host "Skipping $($identityFiles.Count) wwwroot/Identity files (locked when site runs). Stop site first for full deploy." -ForegroundColor Yellow
}

if ($includeSet -ne $null) {
    $before = $files.Count
    $files = @($files | Where-Object {
        $relNorm = $_.FullName.Substring($SourcePath.Length).TrimStart("\", "/").Replace("\", "/")
        $includeSet.Contains($relNorm)
    })
    Write-Host "Include-manifest filter: kept $($files.Count) of $before file(s)." -ForegroundColor Cyan
}

$timeFilterUtc = $null
if ($ModifiedWithinHours -gt 0) {
    $timeFilterUtc = [DateTime]::UtcNow.AddHours(-$ModifiedWithinHours)
    $before = $files.Count
    $files = @($files | Where-Object { $_.LastWriteTimeUtc -ge $timeFilterUtc })
    Write-Host "Time filter (UTC): only files modified in the last $ModifiedWithinHours hour(s) (cutoff $($timeFilterUtc.ToString('o'))). Kept $($files.Count) of $before." -ForegroundColor Cyan
}

# Incremental deploy: only upload changed files (compare size + LastWriteTimeUtc)
$manifestPath = Join-Path $PSScriptRoot ".deploy-manifest.json"
$manifest = @{}
if (-not $ForceFull -and $ModifiedWithinHours -eq 0 -and (Test-Path $manifestPath)) {
    try {
        $raw = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $raw.PSObject.Properties | ForEach-Object { $manifest[$_.Name] = @{ size = $_.Value.size; modified = $_.Value.modified } }
    } catch { $manifest = @{} }
}
if ($ForceFull) { Write-Host "Force full deploy (all files)" -ForegroundColor Yellow
} elseif ($ModifiedWithinHours -gt 0) { Write-Host "Recent-files deploy (no manifest skip)" -ForegroundColor Cyan
} else { Write-Host "Incremental deploy (changed files only)" -ForegroundColor Cyan }

$toUpload = @()
foreach ($f in $files) {
    $rel = $f.FullName.Substring($SourcePath.Length).TrimStart("\", "/").Replace("\", "/")
    if ($rel -match "\.(user|pubxml)$") { continue }
    $key = $rel
    $prev = $manifest[$key]
    $size = $f.Length
    $modified = $f.LastWriteTimeUtc.ToString("o")
    if ($ModifiedWithinHours -gt 0) {
        $toUpload += $f
    } elseif ($ForceFull -or -not $prev -or $prev.size -ne $size -or $prev.modified -ne $modified) {
        $toUpload += $f
    }
}

$total = $toUpload.Count
$consideredFiles = ($files | Where-Object { $_.FullName -notmatch "\.(user|pubxml)$" }).Count
$skipped = $consideredFiles - $total
if ($skipped -gt 0) { Write-Host "Skipping $skipped unchanged files" -ForegroundColor Gray }
if ($total -eq 0) {
    Write-Host "No files selected for upload." -ForegroundColor Yellow
    exit 0
}
Write-Host "Uploading $total files..." -ForegroundColor Cyan

$i = 0
$dirsCache = @{}
foreach ($f in $toUpload) {
    $i++
    $rel = $f.FullName.Substring($SourcePath.Length).TrimStart("\")
    $relNorm = $rel.Replace("\", "/")
    $remote = "$RemotePath/" + $relNorm
    $dir = Split-Path $remote -Parent
    if ($dir -and $dir -ne $RemotePath -and -not $dirsCache[$dir]) {
        $parts = $dir.Replace($RemotePath, "").TrimStart("/").Split("/")
        $acc = $RemotePath
        foreach ($p in $parts) {
            if ($p) {
                $acc += "/" + $p
                Ensure-FtpDir $acc
            }
        }
        $dirsCache[$dir] = $true
    }
    Write-Host "[$i/$total] $rel"
    try {
        Write-FtpFile -LocalPath $f.FullName -RemotePath $remote
        $manifest[$relNorm] = @{ size = $f.Length; modified = $f.LastWriteTimeUtc.ToString("o") }
    } catch {
        Write-Host "  ERROR: $_" -ForegroundColor Red
    }
}

# Persist manifest for next incremental deploy (prune entries for deleted files). Skip update when only doing a time-window push.
if ($ModifiedWithinHours -eq 0) {
    $keysToRemove = @()
    foreach ($k in $manifest.Keys) {
        $localPath = Join-Path $SourcePath $k.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path $localPath)) { $keysToRemove += $k }
    }
    foreach ($k in $keysToRemove) { $manifest.Remove($k) }
    $manifest | ConvertTo-Json -Depth 3 | Set-Content $manifestPath -Encoding UTF8
}

Write-Host "`nUpload complete. Restart the site in SmarterASP Control Panel." -ForegroundColor Green
