param(
    [string]$Version = "1.0.15",
    [string]$BindAddress = "127.0.0.1",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$releaseRoot = Join-Path $projectRoot "publish\Releases\VehiclePermitSystem-$Version-win-x64"
$deliverablesRoot = Join-Path $releaseRoot "deliverables"
$operationsGuideSource = Join-Path $scriptRoot "README-AR.md"
$operationsGuideName = "README-AR.md"
$installerName = "VehiclePermitSystem-Setup-$Version.exe"

& (Join-Path $scriptRoot "package-release.ps1") -Version $Version -BindAddress $BindAddress -Port $Port

New-Item -ItemType Directory -Path $deliverablesRoot -Force | Out-Null

$zipPath = "$releaseRoot.zip"
if (Test-Path $zipPath) {
    Copy-Item $zipPath (Join-Path $deliverablesRoot ([IO.Path]::GetFileName($zipPath))) -Force
}

if (Test-Path $operationsGuideSource) {
    Copy-Item $operationsGuideSource (Join-Path $deliverablesRoot $operationsGuideName) -Force
}

$setupBuilt = $true
try {
    & (Join-Path $scriptRoot "build-setup.ps1") -Version $Version
}
catch {
    $setupBuilt = $false
    $_.Exception.Message | Set-Content -Path (Join-Path $deliverablesRoot "SETUP-NOT-BUILT.txt") -Encoding UTF8
}

$installerRoot = Join-Path $releaseRoot "installer"
if (Test-Path $installerRoot) {
    Get-ChildItem $installerRoot -Filter "*.exe" -File |
        Where-Object { $_.Name -ne $installerName } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $installerPath = Join-Path $installerRoot $installerName
    if (Test-Path $installerPath) {
        Copy-Item $installerPath (Join-Path $deliverablesRoot $installerName) -Force
    }
}

$summary = @(
    "Version: $Version",
    "ZIP: $(if (Test-Path $zipPath) { [IO.Path]::GetFileName($zipPath) } else { 'missing' })",
    "SetupBuilt: $setupBuilt",
    "SetupName: $(if (Test-Path (Join-Path $deliverablesRoot $installerName)) { $installerName } else { 'missing' })",
    "InstallerLog: %ProgramData%\Vehicle Permit System\logs\installer.log",
    "StartupModes: WindowsService -> ScheduledTask fallback",
    "Guide: $(if (Test-Path (Join-Path $deliverablesRoot $operationsGuideName)) { $operationsGuideName } else { 'missing' })",
    "Deliverables: $deliverablesRoot"
) -join [Environment]::NewLine

$summary | Set-Content -Path (Join-Path $deliverablesRoot "delivery-summary.txt") -Encoding UTF8

Write-Host "Delivery folder prepared:" -ForegroundColor Green
Write-Host $deliverablesRoot -ForegroundColor Cyan
