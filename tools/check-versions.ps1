# Checks that the Catalyst app version is identical in all three manifests:
#   package.json, src-tauri/tauri.conf.json, src-tauri/Cargo.toml
# plus reports the pinned FFmpeg version for release notes.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-versions.ps1
param(
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrWhiteSpace($scriptDir)) { $scriptDir = 'tools' }
    $RepoRoot = Join-Path $scriptDir '..'
}

$pkg = Get-Content -LiteralPath (Join-Path $RepoRoot 'package.json') -Raw | ConvertFrom-Json
$tauri = Get-Content -LiteralPath (Join-Path $RepoRoot 'src-tauri/tauri.conf.json') -Raw | ConvertFrom-Json
$cargoText = Get-Content -LiteralPath (Join-Path $RepoRoot 'src-tauri/Cargo.toml') -Raw
$ffmpeg = Get-Content -LiteralPath (Join-Path $RepoRoot 'tools/ffmpeg-manifest.json') -Raw | ConvertFrom-Json

if ($cargoText -match '(?m)^version\s*=\s*"([^"]+)"') { $cargoVer = $Matches[1] }
else { throw 'Could not parse version from src-tauri/Cargo.toml' }

Write-Host "package.json:            $($pkg.version)"
Write-Host "src-tauri/tauri.conf.json: $($tauri.version)"
Write-Host "src-tauri/Cargo.toml:      $cargoVer"
Write-Host "ffmpeg pin:                $($ffmpeg.pinnedVersion) ($($ffmpeg.expectedVersionString))"

$ok = $true
if ($pkg.version -ne $tauri.version) { Write-Host 'MISMATCH: package.json != tauri.conf.json'; $ok = $false }
if ($pkg.version -ne $cargoVer) { Write-Host 'MISMATCH: package.json != Cargo.toml'; $ok = $false }

if (-not $ok) {
    throw 'Version mismatch. See docs/RELEASING.md: all three versions must be identical before tagging.'
}

# Tauri requires semver; beta tags are v<version>-beta.N (e.g. v0.1.0-beta.1).
if ($pkg.version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$') {
    throw "Version '$($pkg.version)' is not semver."
}

Write-Host 'Versions in sync.'
