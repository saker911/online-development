param(
    [string]$ServiceName = "VehiclePermitSystem",
    [string]$TaskName = "VehiclePermitSystemStartup"
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "installer-common.ps1")

Assert-Administrator

$context = Get-InstallerContext -ServiceName $ServiceName -TaskName $TaskName
$logPath = Get-InstallerLogPath -Context $context

Write-InstallerLog -Message "بدء إزالة خدمة النظام والمهام المجدولة." -LogPath $logPath
Remove-ServiceIfExists -ServiceName $context.ServiceName -LogPath $logPath
Stop-LegacyServicesIfExists -Context $context -LogPath $logPath
Unregister-ScheduledTaskIfExists -Context $context -LogPath $logPath
Stop-ExecutableProcesses -Context $context -LogPath $logPath
Write-InstallerLog -Message "تمت إزالة مكونات التشغيل مع الإبقاء على بيانات ProgramData." -LogPath $logPath
