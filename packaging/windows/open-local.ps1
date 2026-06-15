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
$startScript = Join-Path $PSScriptRoot "start-background.ps1"
$runtimeConfigPath = Join-Path (Resolve-StorageRoot -Context $context) $context.RuntimeConfigRelativePath
$resolvedPort = Resolve-ConfiguredPort -RuntimeConfigPath $runtimeConfigPath -FallbackPort $Port
$applicationUrl = "http://localhost:$resolvedPort"

if (-not (Test-Path $appPath)) {
    throw "Application executable not found: $appPath"
}

$service = Get-Service -Name $context.ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        Start-Service -Name $context.ServiceName
        Write-InstallerLog -Message "تم طلب تشغيل خدمة Windows قبل فتح النظام." -LogPath $logPath
    }
}
else {
    $task = Get-RegisteredStartupTask -Context $context
    if ($task) {
        Start-StartupScheduledTask -Context $context -LogPath $logPath
    }
    else {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $startScript -BindAddress $BindAddress -Port $resolvedPort | Out-Null
        Write-InstallerLog -Message "تم تشغيل التطبيق مباشرة في الخلفية قبل فتح الصفحة." -LogPath $logPath
    }
}

if (-not (Wait-ApplicationReady -Url $applicationUrl -TimeoutSeconds 60 -LogPath $logPath)) {
    throw "التطبيق لم يصبح جاهزًا على $applicationUrl"
}

Start-Process $applicationUrl