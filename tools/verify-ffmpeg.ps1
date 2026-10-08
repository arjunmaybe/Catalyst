# Verifies the locally cached FFmpeg sidecar against tools/ffmpeg-manifest.json
# without downloading anything. Fails if the exe is missing, stale, or
# reports an unexpected version/configuration.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-ffmpeg.ps1
#   powershell ... verify-ffmpeg.ps1 -ManifestPath tools/ffmpeg-manifest.json -ExePath src-tauri/binaries/ffmpeg-x86_64-pc-windows-msvc.exe
param(
    [string]$ManifestPath = '',
    [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'

# Runtime default resolution (see fetch-ffmpeg.ps1: $PSScriptRoot can be
# empty under nested `powershell -File` invocations).
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($scriptDir)) { $scriptDir = 'tools' }
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = Join-Path $scriptDir 'ffmpeg-manifest.json' }
if ([string]::IsNullOrWhiteSpace($ExePath)) { $ExePath = Join-Path $scriptDir '../src-tauri/binaries/ffmpeg-x86_64-pc-windows-msvc.exe' }

function Invoke-FfmpegText([string]$Exe, [string[]]$FfmpegArgs) {
    # See fetch-ffmpeg.ps1: merge inside cmd.exe so ffmpeg's stderr banner
    # arrives as plain text instead of NativeCommandError records.
    $argLine = ($FfmpegArgs | ForEach-Object { '"{0}"' -f $_ }) -join ' '
    $text = cmd /c "`"$Exe`" $argLine 2>&1"
    return ($text | Out-String)
}

if (-not (Test-Path -LiteralPath $ManifestPath)) { throw "Manifest not found: $ManifestPath" }
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$expected = $manifest.expectedVersionString

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "FFmpeg sidecar missing at $ExePath. Run fetch-ffmpeg.ps1 once with internet, or set CATALYST_SKIP_FFMPEG=1 for an offline build (media conversion then needs ffmpeg on PATH)."
}

$version = Invoke-FfmpegText $ExePath @('-version')
$firstLine = ($version -split "`r?`n" | Select-Object -First 1).Trim()
Write-Host $firstLine

if ($version -notlike "*$expected*") {
    throw "FFmpeg version mismatch.`nExpected substring: $expected`nGot: $firstLine"
}
if ($version -notlike '*--enable-gpl*--enable-version3*') {
    throw "FFmpeg build configuration mismatch: expected --enable-gpl --enable-version3 in -version output."
}

$license = Invoke-FfmpegText $ExePath @('-L')
if ($license -notmatch 'General Public License') {
    throw 'FFmpeg -L output does not mention the GPL. Refusing to treat this binary as the pinned GPL build.'
}

$hash = (Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "exe SHA256: $hash"

$auditPath = "$ExePath.version.json"
if (Test-Path -LiteralPath $auditPath) {
    Write-Host "audit file: $auditPath"
    Get-Content -LiteralPath $auditPath | Write-Host
}

Write-Host 'FFmpeg sidecar OK.'
