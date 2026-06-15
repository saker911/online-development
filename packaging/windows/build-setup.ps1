param(
    [string]$Version = "1.0.15"
)

$ErrorActionPreference = "Stop"

function Resolve-InnoCompiler {
    $candidates = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$releaseRoot = Join-Path $projectRoot "publish\Releases\VehiclePermitSystem-$Version-win-x64"
$appSourceDir = Join-Path $releaseRoot "app"
$installerOutputDir = Join-Path $releaseRoot "installer"
$issPath = Join-Path $PSScriptRoot "VehiclePermitSystemWeb.iss"

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is required to build the installer."
}

if (-not (Test-Path $appSourceDir)) {
    throw "Published app folder not found: $appSourceDir"
}

$iscc = Resolve-InnoCompiler
if (-not $iscc) {
    throw "Inno Setup Compiler (ISCC.exe) was not found. Install Inno Setup 6 or add ISCC.exe to PATH."
}

New-Item -ItemType Directory -Path $installerOutputDir -Force | Out-Null

$env:VPS_APP_VERSION = $Version
$env:VPS_APP_SOURCE_DIR = $appSourceDir
$env:VPS_INSTALLER_OUTPUT_DIR = $installerOutputDir

if ([string]::IsNullOrWhiteSpace($env:VPS_APP_VERSION) -or [string]::IsNullOrWhiteSpace($env:VPS_APP_SOURCE_DIR) -or [string]::IsNullOrWhiteSpace($env:VPS_INSTALLER_OUTPUT_DIR)) {
    throw "Installer environment variables are incomplete. VPS_APP_VERSION, VPS_APP_SOURCE_DIR, and VPS_INSTALLER_OUTPUT_DIR are required."
}

& $iscc $issPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup Compiler failed with exit code $LASTEXITCODE."
}

$installerFileName = "VehiclePermitSystem-Setup-$Version.exe"
$installerPath = Join-Path $installerOutputDir $installerFileName
$installer = if (Test-Path $installerPath) {
    Get-Item $installerPath
}
else {
    Get-ChildItem $installerOutputDir -Filter "*.exe" -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

if (-not $installer) {
    throw "Inno Setup completed without producing an installer in: $installerOutputDir"
}

# Keep exactly one canonical installer artifact per version.
if ($installer.FullName -ne $installerPath) {
    Copy-Item $installer.FullName $installerPath -Force
    $installer = Get-Item $installerPath
}

Get-ChildItem $installerOutputDir -Filter "*.exe" -File |
    Where-Object { $_.FullName -ne $installer.FullName } |
    Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Installer created in:" -ForegroundColor Green
Write-Host $installerOutputDir -ForegroundColor Cyan
Write-Host "Installer file:" -ForegroundColor Green
Write-Host $installer.FullName -ForegroundColor Cyan
