# Hashes Windows release artifacts deterministically (SHA256) so a beta
# can be verified after download. Run after `npx tauri build`.
#
# Outputs <file>.sha256 next to each artifact and prints a manifest block
# suitable for pasting into GitHub release notes.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/hash-artifacts.ps1
#   powershell ... hash-artifacts.ps1 -BundleDir src-tauri/target/release/bundle
param(
    [string]$BundleDir = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($BundleDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrWhiteSpace($scriptDir)) { $scriptDir = 'tools' }
    $BundleDir = Join-Path $scriptDir '../src-tauri/target/release/bundle'
}

if (-not (Test-Path -LiteralPath $BundleDir)) {
    throw "Bundle directory not found: $BundleDir. Run 'npx tauri build' first."
}

$files = Get-ChildItem -Path $BundleDir -Recurse -File -Include '*.exe', '*.msi', '*.sig', '*.zip' |
    Where-Object { $_.Extension -ne '.sha256' } |
    Sort-Object FullName

if (-not $files -or $files.Count -eq 0) {
    throw "No release artifacts (*.exe/*.msi) found under $BundleDir."
}

foreach ($f in $files) {
    $hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $out = "$($f.FullName).sha256"
    "$hash  $($f.Name)" | Set-Content -LiteralPath $out -Encoding ASCII
    Write-Host "$($f.Name): $hash"
}

Write-Host ''
Write-Host 'Paste into release notes:'
Write-Host '```'
foreach ($f in $files) {
    $hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "$($f.Name)  SHA256: $hash"
}
Write-Host '```'
