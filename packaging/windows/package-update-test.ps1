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

& (Join-Path $scriptRoot "package-all.ps1") -Version $Version -BindAddress $BindAddress -Port $Port

New-Item -ItemType Directory -Path $deliverablesRoot -Force | Out-Null

$updateTestNotes = @(
    "Update test package",
    "Version: $Version",
    "BindAddress: $BindAddress",
    "Port: $Port",
    "",
    "Install this package over an existing Vehicle Permit System installation to verify that service upgrade preserves external data.",
    "Install path: C:\Program Files\Vehicle Permit System",
    "Database path: C:\ProgramData\Vehicle Permit System\data\vehicle-permit-system.db",
    "Uploads path: C:\ProgramData\Vehicle Permit System\uploads"
) -join [Environment]::NewLine

$updateTestNotes | Set-Content -Path (Join-Path $deliverablesRoot "update-test-notes.txt") -Encoding UTF8

Write-Host "Update test package prepared:" -ForegroundColor Green
Write-Host $deliverablesRoot -ForegroundColor Cyan
