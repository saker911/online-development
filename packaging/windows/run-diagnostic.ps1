param(
    [string]$BindAddress = "127.0.0.1",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "installer-common.ps1")

$context = Get-InstallerContext
$appRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $appRoot $context.ExecutableName
$runtimeConfigPath = Join-Path (Resolve-StorageRoot -Context $context) $context.RuntimeConfigRelativePath

[System.Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', 'Process')
[System.Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Production', 'Process')

if (-not (Test-Path $appPath)) {
    throw "لم يتم العثور على الملف التنفيذي: $appPath"
}

if (Test-Path $runtimeConfigPath) {
    $configuredUrl = Resolve-ConfiguredUrl -RuntimeConfigPath $runtimeConfigPath -BindAddress $BindAddress -Port $Port
    [System.Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', $configuredUrl, 'Process')
}

if ([string]::IsNullOrWhiteSpace([System.Environment]::GetEnvironmentVariable('ASPNETCORE_URLS', 'Process'))) {
    [System.Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', ('http://' + $BindAddress + ':' + $Port), 'Process')
}

Write-Host ("Running Vehicle Permit System diagnostics on " + [System.Environment]::GetEnvironmentVariable('ASPNETCORE_URLS', 'Process'))
Write-Host "Press Ctrl+C to stop."
& $appPath
