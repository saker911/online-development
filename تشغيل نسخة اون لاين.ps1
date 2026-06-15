$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://127.0.0.1:5001"
$env:VehiclePermitSystemWeb__StorageRoot = Join-Path $projectRoot ".online-storage"

dotnet run --no-launch-profile
