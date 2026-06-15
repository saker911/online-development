param(
    [string]$BindAddress = "127.0.0.1",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "installer-common.ps1")

$context = Get-InstallerContext
$logPath = Get-InstallerLogPath -Context $context
$appRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $appRoot $context.ExecutableName
$runtimeConfigPath = Join-Path (Resolve-StorageRoot -Context $context) $context.RuntimeConfigRelativePath

[System.Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', 'Process')
[System.Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Production', 'Process')

if (-not (Test-Path $appPath)) {
    throw "لم يتم العثور على الملف التنفيذي: $appPath"
}

if (Test-Path $runtimeConfigPath) {
    try {
        $configuredUrl = Resolve-ConfiguredUrl -RuntimeConfigPath $runtimeConfigPath -BindAddress $BindAddress -Port $Port
        [System.Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', $configuredUrl, 'Process')
        $Port = Resolve-ConfiguredPort -RuntimeConfigPath $runtimeConfigPath -FallbackPort $Port
    }
    catch {
        Write-InstallerLog -Message ("تعذر قراءة runtime config: " + $_.Exception.Message) -Level "WARN" -LogPath $logPath
    }
}

if ([string]::IsNullOrWhiteSpace([System.Environment]::GetEnvironmentVariable('ASPNETCORE_URLS', 'Process'))) {
    [System.Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', ('http://' + $BindAddress + ':' + $Port), 'Process')
}

if (Test-PortInUse -CheckPort $Port) {
    $owners = Get-PortOwners -CheckPort $Port
    $appOwner = $owners | Where-Object { $_.ProcessName -eq $context.ExecutableProcessName }
    if ($appOwner) {
        Write-InstallerLog -Message "التطبيق يعمل بالفعل على المنفذ المطلوب؛ لن يتم بدء نسخة إضافية." -LogPath $logPath
        exit 0
    }

    $ownerSummary = ($owners | ForEach-Object { $_.ProcessName + " (" + $_.Id + ")" }) -join ", "
    throw "المنفذ $Port مستخدم بواسطة عملية أخرى: $ownerSummary"
}

Write-InstallerLog -Message ("بدء التطبيق في الخلفية على " + [System.Environment]::GetEnvironmentVariable('ASPNETCORE_URLS', 'Process')) -LogPath $logPath
Start-Process -FilePath $appPath -WorkingDirectory $appRoot -WindowStyle Hidden