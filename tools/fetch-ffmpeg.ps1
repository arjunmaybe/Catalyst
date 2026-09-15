# Downloads a Windows ffmpeg build zip and extracts just ffmpeg.exe to $DestinationExe.
# Skips work when the exe already exists. Used by src-tauri/build.rs (EnsureFfmpeg).
param(
    [Parameter(Mandatory = $true)][string]$ZipUrl,
    [Parameter(Mandatory = $true)][string]$DestinationExe
)

$ErrorActionPreference = 'Stop'

if (Test-Path -LiteralPath $DestinationExe) {
    Write-Host "ffmpeg already present at $DestinationExe, skipping download."
    exit 0
}

$tmpZip = Join-Path $env:TEMP ("catalyst-ffmpeg-" + [Guid]::NewGuid().ToString('N') + '.zip')
$tmpDir = Join-Path $env:TEMP ("catalyst-ffmpeg-" + [Guid]::NewGuid().ToString('N'))

try {
    Write-Host "Downloading ffmpeg from $ZipUrl ..."
    Invoke-WebRequest -Uri $ZipUrl -OutFile $tmpZip
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force

    $exe = Get-ChildItem -Path $tmpDir -Filter 'ffmpeg.exe' -Recurse | Select-Object -First 1
    if (-not $exe) { throw 'ffmpeg.exe not found inside the downloaded archive.' }

    $destDir = Split-Path -Parent $DestinationExe
    if ($destDir -and -not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    Copy-Item -LiteralPath $exe.FullName -Destination $DestinationExe -Force
    Write-Host "ffmpeg installed to $DestinationExe"
}
finally {
    Remove-Item -LiteralPath $tmpZip -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
}
