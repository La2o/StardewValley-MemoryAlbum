<#
.SYNOPSIS
Builds the mod and uploads the release zip to Nexus Mods as a new version of the existing main file.

.DESCRIPTION
Uses the Nexus Mods v3 Upload API (open beta), following the same steps as the official
Nexus-Mods/upload-action: create a multipart upload, PUT the parts, complete it, finalise it,
wait until it's processed, then attach it as a new file version and add the changelog.

The Upload API can only add versions to a file that already exists, so the first release must be
uploaded by hand on the website. Then put the mod and file IDs in tools/nexus.json.

The API key is read from the NEXUS_API_KEY environment variable (create one at
https://www.nexusmods.com/settings/api-keys). It's never printed or written to disk.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/publish-nexus.ps1 -DryRun
Builds and validates everything without contacting Nexus.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/publish-nexus.ps1
Builds, shows what will be uploaded, asks for confirmation, then uploads.
#>
param(
    # Build and validate, but don't contact Nexus.
    [switch]$DryRun,

    # Skip the confirmation prompt.
    [switch]$Yes,

    # Use the existing zip instead of building.
    [switch]$SkipBuild,

    # Archive the previous version of the file when uploading.
    [switch]$ArchivePrevious
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$apiBase = if ($env:NEXUSMODS_API_BASE) { $env:NEXUSMODS_API_BASE.TrimEnd('/') } else { 'https://api.nexusmods.com/v3' }

function Fail([string]$message) {
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

function Step([string]$message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Invoke-NexusApi([string]$method, [string]$path, $body) {
    $params = @{
        Uri         = "$apiBase$path"
        Method      = $method
        Headers     = @{ apikey = $env:NEXUS_API_KEY; 'User-Agent' = 'MemoryAlbum-publish-script' }
        ContentType = 'application/json'
    }
    if ($null -ne $body) {
        $params.Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 10 -Compress))
    }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $detail = if ($_.ErrorDetails) { $_.ErrorDetails.Message } else { $_.Exception.Message }
        Fail "$method $path failed: $detail"
    }
}

# ---------- read project info ----------
$manifest = Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.Version
$nexus = Get-Content (Join-Path $PSScriptRoot 'nexus.json') -Raw | ConvertFrom-Json

# changelog section for this version: lines under "## <version>" until the next "## " heading
$changelogLines = Get-Content (Join-Path $root 'CHANGELOG.md')
$start = -1
for ($i = 0; $i -lt $changelogLines.Count; $i++) {
    if ($changelogLines[$i] -match "^##\s+$([regex]::Escape($version))(\s|$)") { $start = $i; break }
}
if ($start -lt 0) {
    Fail "CHANGELOG.md has no '## $version' section. Add release notes before publishing."
}
$end = $changelogLines.Count
for ($i = $start + 1; $i -lt $changelogLines.Count; $i++) {
    if ($changelogLines[$i] -match '^##\s') { $end = $i; break }
}
$changelog = ($changelogLines[($start + 1)..($end - 1)] -join "`n").Trim()
if ($changelogLines[$start] -match 'unreleased') {
    Write-Host "WARNING: the CHANGELOG heading for $version still says 'unreleased'." -ForegroundColor Yellow
}

# ---------- validate config ----------
$problems = @()
if (-not $nexus.ModId) { $problems += 'tools/nexus.json: ModId is not set.' }
if (-not $nexus.FileId) { $problems += 'tools/nexus.json: FileId is not set.' }
if (-not $nexus.ApiModId) { $problems += 'tools/nexus.json: ApiModId is not set (the "Unique Mod ID" under Advanced).' }
if ($nexus.ModId -and -not ($manifest.UpdateKeys -contains "Nexus:$($nexus.ModId)")) {
    $problems += "manifest.json: UpdateKeys should contain ""Nexus:$($nexus.ModId)"" so SMAPI can check for updates."
}
if (-not $DryRun -and -not $env:NEXUS_API_KEY) {
    $problems += 'The NEXUS_API_KEY environment variable is not set.'
}
if ($problems.Count -gt 0) {
    if ($DryRun) {
        $problems | ForEach-Object { Write-Host "WARNING: $_" -ForegroundColor Yellow }
    }
    else {
        $problems | ForEach-Object { Write-Host "ERROR: $_" -ForegroundColor Red }
        exit 1
    }
}

# ---------- build ----------
$zip = Join-Path $root "bin\Release\net6.0\MemoryAlbum $version.zip"
if (-not $SkipBuild) {
    Step "Building Release $version"
    & dotnet build (Join-Path $root 'MemoryAlbum.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail 'Build failed.' }
}
if (-not (Test-Path $zip)) { Fail "Release zip not found: $zip" }
$zipInfo = Get-Item $zip

# ---------- summary ----------
Write-Host ''
Write-Host "Mod:        $($manifest.Name) $version"
Write-Host "Zip:        $($zipInfo.FullName) ($([math]::Round($zipInfo.Length / 1KB, 1)) KB)"
Write-Host "Nexus:      mod $($nexus.ModId), file $($nexus.FileId) (main file)"
Write-Host "Archive previous version: $([bool]$ArchivePrevious)"
Write-Host 'Changelog:'
$changelog -split "`n" | ForEach-Object { Write-Host "  $_" }
Write-Host ''

if ($DryRun) {
    Step 'Dry run: nothing was uploaded.'
    exit 0
}

if (-not $Yes) {
    $answer = Read-Host "Upload $($manifest.Name) $version to Nexus Mods? This publishes it publicly. (y/N)"
    if ($answer -notmatch '^(y|yes)$') { Write-Host 'Cancelled.'; exit 0 }
}

# ---------- 1. create multipart upload ----------
Step 'Creating upload'
$created = (Invoke-NexusApi 'POST' '/uploads/multipart' @{ filename = $zipInfo.Name; size_bytes = [string]$zipInfo.Length }).data
$uploadId = $created.id
$partSize = [int64]$created.part_size_bytes
$partUrls = @($created.part_presigned_urls)
Write-Host "    upload ${uploadId}: $($partUrls.Count) part(s)"

# ---------- 2. upload parts ----------
$bytes = [IO.File]::ReadAllBytes($zipInfo.FullName)
$etags = @()
for ($i = 0; $i -lt $partUrls.Count; $i++) {
    $offset = [int64]$i * $partSize
    $length = [int][math]::Min($partSize, $bytes.Length - $offset)
    $chunk = New-Object byte[] $length
    [Array]::Copy($bytes, $offset, $chunk, 0, $length)

    Step "Uploading part $($i + 1)/$($partUrls.Count) ($length bytes)"
    try {
        $response = Invoke-WebRequest -Uri $partUrls[$i] -Method Put -Body $chunk -ContentType 'application/octet-stream' -UseBasicParsing
    }
    catch {
        Fail "Uploading part $($i + 1) failed: $($_.Exception.Message)"
    }
    $etag = $response.Headers['ETag']
    if (-not $etag) { Fail "No ETag returned for part $($i + 1)." }
    $etags += [pscustomobject]@{ Number = $i + 1; ETag = ($etag -replace '"', '') }
}

# ---------- 3. complete multipart upload ----------
Step 'Completing upload'
$xml = "<CompleteMultipartUpload>`n" + (($etags | ForEach-Object { "  <Part>`n    <PartNumber>$($_.Number)</PartNumber>`n    <ETag>$($_.ETag)</ETag>`n  </Part>" }) -join "`n") + "`n</CompleteMultipartUpload>"
try {
    Invoke-WebRequest -Uri $created.complete_presigned_url -Method Post -Body $xml -ContentType 'application/xml' -UseBasicParsing | Out-Null
}
catch {
    Fail "Completing the upload failed: $($_.Exception.Message)"
}

# ---------- 4. finalise ----------
Step 'Finalising upload'
Invoke-NexusApi 'POST' "/uploads/$uploadId/finalise" $null | Out-Null

# ---------- 5. wait for processing (virus scan etc.) ----------
$delay = 2.0
for ($attempt = 0; ; $attempt++) {
    $state = (Invoke-NexusApi 'GET' "/uploads/$uploadId" $null).data.state
    Write-Host "    state: $state"
    if ($state -eq 'available') { break }
    if ($attempt -ge 60) { Fail "Timed out waiting for upload $uploadId to be processed." }
    Start-Sleep -Seconds ([math]::Min($delay, 30))
    $delay *= 1.5
}

# ---------- 6. add as a new version of the main file ----------
Step "Creating file version $version"
$fileVersion = Invoke-NexusApi 'POST' "/mod-files/$($nexus.FileId)/versions" @{
    upload_id             = $uploadId
    name                  = "$($manifest.Name) $version"
    description           = $manifest.Description
    version               = $version
    file_category         = 'main'
    archive_existing_file = [bool]$ArchivePrevious
    update_mod_version    = $true
}
Write-Host "    version id: $($fileVersion.data.version.id)"

# ---------- 7. changelog ----------
if ($changelog) {
    Step 'Adding changelog'
    Invoke-NexusApi 'POST' "/mods/$($nexus.ApiModId)/changelogs" @{ version = $version; changelog = $changelog } | Out-Null
}

Step "Published $($manifest.Name) ${version}: https://www.nexusmods.com/stardewvalley/mods/$($nexus.ModId)"
