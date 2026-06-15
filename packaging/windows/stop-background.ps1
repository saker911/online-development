$ErrorActionPreference = "SilentlyContinue"

$service = Get-Service -Name "VehiclePermitSystem" -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
    Stop-Service -Name "VehiclePermitSystem" -Force -ErrorAction SilentlyContinue
}

Get-Process -Name "VehiclePermitSystemWeb" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
