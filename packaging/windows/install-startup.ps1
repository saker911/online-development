param(
    [string]$TaskName = "VehiclePermitSystemStartup",
    [string]$BindAddress = "127.0.0.1",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "install-service.ps1") -TaskName $TaskName -BindAddress $BindAddress -Port $Port -ForceScheduledTask