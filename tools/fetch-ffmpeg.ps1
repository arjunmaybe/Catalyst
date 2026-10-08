# Catalyst FFmpeg sidecar fetcher (Windows).
#
# Pinned, checksum-verified download driven by tools/ffmpeg-manifest.json.
# Used by src-tauri/build.rs (EnsureFfmpeg) and by CI release builds.
#
# Behaviour:
#  - If $DestinationExe already exists AND reports the manifest's
#    expectedVersionString, the download is skipped (cached build).
#  - If it exists but reports a different version (e.g. stale 9.0.1
#    after the manifest moved to 9.0.2), it is replaced, unless
#    -KeepExisting is passed (then the script fails loudly instead).
#  - The zip's SHA256 must match the manifest's zipSha256 exactly.
#  - After extraction, `ffmpeg -version` must contain expectedVersionString.
#  - A JSON audit file is written next to the exe:
#      <DestinationExe>.version.json  (ffmpeg -version first line, exe SHA256, manifest version, zip URL)
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/fetch-ffmpeg.ps1 `
#     -ManifestPath tools/ffmpeg-manifest.json `
#     -DestinationExe src-tauri/binaries/ffmpeg-x86_64-pc-windows-msvc.exe
#
# Manual override (not used by the build; for emergency mirroring only):
#   ... -ZipUrl <url> -ExpectedSha256 <hex> -ExpectedVersionString <substring>
#   When -ZipUrl is given, the manifest is ignored for URL/hash/version.
param(
    [string]$ManifestPath = '',
    [Parameter(Mandatory = $true)][string]$DestinationExe,
    [switch]$Force,
    [switch]$KeepExisting,
    [string]$ZipUrl = '',
    [string]$ExpectedSha256 = '',
    [string]$ExpectedVersionString = ''
)

$ErrorActionPreference = 'Stop'

# Resolve the manifest default at runtime ($PSScriptRoot is empty when the
# script is launched via a nested `powershell -File` call, so do not use it
# as a parameter default expression).
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrWhiteSpace($scriptDir)) { $scriptDir = 'tools' }
    $ManifestPath = Join-Path $scriptDir 'ffmpeg-manifest.json'
}

function Invoke-FfmpegText([string]$Exe, [string[]]$FfmpegArgs) {
    # Capture stdout+stderr via cmd.exe merging: ffmpeg writes its banner
    # to stderr, and Windows PowerShell 5.1 turns redirected-stderr lines
    # into ErrorRecords (terminating under $ErrorActionPreference='Stop',
    # red noise otherwise). Merging inside cmd.exe returns plain text.
    $argLine = ($FfmpegArgs | ForEach-Object { '"{0}"' -f $_ }) -join ' '
    $text = cmd /c "`"$Exe`" $argLine 2>&1"
    return ($text | Out-String)
}

function Get-ExeVersionLine([string]$Exe) {
    $out = Invoke-FfmpegText $Exe @('-version')
    return ($out -split "`r?`n" | Select-Object -First 1).Trim()
}

# Resolve pin source: manifest by default, explicit override when -ZipUrl is set.
$manifestVersion = ''
$zipSha = ''
if ($ZipUrl -ne '') {
    if ($ExpectedSha256 -eq '' -or $ExpectedVersionString -eq '') {
        throw 'When -ZipUrl is supplied, -ExpectedSha256 and -ExpectedVersionString are required.'
    }
    Write-Host "Using explicit override URL (manifest ignored for URL/hash/version): $ZipUrl"
    $zipSha = $ExpectedSha256
} else {
    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        throw "FFmpeg manifest not found at $ManifestPath"
    }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $ZipUrl = $manifest.zipUrl
    $zipSha = $manifest.zipSha256
    $ExpectedVersionString = $manifest.expectedVersionString
    $manifestVersion = $manifest.pinnedVersion
    if (-not $ZipUrl -or -not $zipSha -or -not $ExpectedVersionString) {
        throw "Manifest $ManifestPath is missing zipUrl/zipSha256/expectedVersionString."
    }
    Write-Host "Pinned FFmpeg: $manifestVersion ($ExpectedVersionString)"
    Write-Host "Zip: $ZipUrl"
}

# Fast path: cached exe already matches the pin.
if ((Test-Path -LiteralPath $DestinationExe) -and (-not $Force)) {
    try {
        $line = Get-ExeVersionLine $DestinationExe
    } catch {
        $line = ''
    }
    if ($line -like "*$ExpectedVersionString*") {
        Write-Host "ffmpeg already present and matches pin ($line), skipping download."
        exit 0
    }
    if ($KeepExisting) {
        throw "Existing ffmpeg at $DestinationExe does not match pin. Got: '$line'. Expected substring: '$ExpectedVersionString'. Re-run without -KeepExisting (or with -Force) to replace it."
    }
    Write-Host "Existing ffmpeg does not match pin (got: '$line'), replacing after verified download..."
}

$destDir = Split-Path -Parent $DestinationExe
if ([string]::IsNullOrWhiteSpace($destDir)) { $destDir = (Get-Location).Path }
if (-not (Test-Path -LiteralPath $destDir)) {
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
}

# Stage the zip next to the destination (same drive) instead of $env:TEMP:
# the gyan.dev essentials zip is ~109 MB and C: may be tight on dev
# machines, while the repo drive normally has room. Do NOT use
# Expand-Archive here: Windows PowerShell 5.1 fails on this zip's
# directory entries, and full extraction wastes ~250 MB. Stream only the
# single bin/ffmpeg.exe entry straight to the destination instead.
$stageTag = "catalyst-ffmpeg-" + [Guid]::NewGuid().ToString('N')
$tmpZip = Join-Path $destDir ($stageTag + '.zip')

try {
    Write-Host "Downloading ffmpeg from $ZipUrl ..."
    Invoke-WebRequest -Uri $ZipUrl -OutFile $tmpZip

    $actual = (Get-FileHash -LiteralPath $tmpZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = $zipSha.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "SHA256 mismatch for ffmpeg zip.`nExpected: $expected`nActual:   $actual`nURL: $ZipUrl"
    }
    Write-Host "SHA256 verified: $actual"

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($tmpZip)
    try {
        $entry = $zip.Entries |
            Where-Object { ($_.FullName -replace '\\', '/') -like '*/ffmpeg.exe' -or ($_.FullName -replace '\\', '/') -eq 'ffmpeg.exe' } |
            Select-Object -First 1
        if (-not $entry) { throw 'ffmpeg.exe not found inside the downloaded archive.' }
        Write-Host "Extracting single entry: $($entry.FullName) ($([math]::Round($entry.Length / 1MB, 1)) MB)"
        $reader = $entry.Open()
        try {
            $writer = [System.IO.File]::Create($DestinationExe)
            try { $reader.CopyTo($writer) }
            finally { $writer.Close() }
        }
        finally { $reader.Close() }
    }
    finally { $zip.Dispose() }

    $versionLine = Get-ExeVersionLine $DestinationExe
    if ($versionLine -notlike "*$ExpectedVersionString*") {
        Remove-Item -LiteralPath $DestinationExe -Force -ErrorAction SilentlyContinue
        throw "Downloaded ffmpeg version mismatch. Got: '$versionLine'. Expected substring: '$ExpectedVersionString'."
    }
    Write-Host "ffmpeg installed to $DestinationExe"
    Write-Host $versionLine

    $exeHash = (Get-FileHash -LiteralPath $DestinationExe -Algorithm SHA256).Hash.ToLowerInvariant()
    $audit = [ordered]@{
        pinnedManifestVersion = $manifestVersion
        expectedVersionString = $ExpectedVersionString
        versionLine           = $versionLine
        exeSha256             = $exeHash
        zipUrl                = $ZipUrl
        zipSha256             = $expected
        fetchedAtUtc          = (Get-Date).ToUniversalTime().ToString('o')
    }
    $auditPath = "$DestinationExe.version.json"
    $audit | ConvertTo-Json | Set-Content -LiteralPath $auditPath -Encoding UTF8
    Write-Host "Wrote audit file: $auditPath"
}
finally {
    Remove-Item -LiteralPath $tmpZip -Force -ErrorAction SilentlyContinue
}
