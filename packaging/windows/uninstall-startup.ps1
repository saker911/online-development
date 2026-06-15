param(
    [string]$TaskName = "VehiclePermitSystemStartup"
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "uninstall-service.ps1") -TaskName $TaskName