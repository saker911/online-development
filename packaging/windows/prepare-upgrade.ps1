param(
    [string]$ServiceName = "VehiclePermitSystem",
    [string]$TaskName = "VehiclePermitSystemStartup",
    [string]$InstallDirectory,
    [string]$HelperScriptPath,
    [string]$BootstrapLogPath,
    [string]$ProbeExecutablePath
)

$ErrorActionPreference = "Continue"

function Write-BootstrapLog {
    param([string]$Message)

    if ([string]::IsNullOrWhiteSpace($BootstrapLogPath)) {
        $BootstrapLogPath = Join-Path $env:ProgramData "Vehicle Permit System\logs\setup-bootstrap.log"
    }

    $bootstrapDirectory = Split-Path -Parent $BootstrapLogPath
    if (-not [string]::IsNullOrWhiteSpace($bootstrapDirectory) -and -not (Test-Path $bootstrapDirectory)) {
        New-Item -ItemType Directory -Path $bootstrapDirectory -Force | Out-Null
    }

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Add-Content -Path $BootstrapLogPath -Value ("[{0}] {1}" -f $timestamp, $Message) -Encoding UTF8
}

$logPath = $null

if ([string]::IsNullOrWhiteSpace($HelperScriptPath)) {
    $HelperScriptPath = Join-Path $PSScriptRoot "installer-common.ps1"
}

try {
    Write-BootstrapLog "prepare-upgrade.ps1 started."
    Write-BootstrapLog ("HelperScriptPath=" + $HelperScriptPath)
    . $HelperScriptPath

    $context = Get-InstallerContext -ServiceName $ServiceName -TaskName $TaskName
    $logPath = Get-InstallerLogPath -Context $context
    Write-BootstrapLog ("InstallerLogPath=" + $logPath)

    Assert-Administrator
    Write-InstallerLog -Message "بدء التحقق من حالة الجهاز قبل التثبيت." -LogPath $logPath

    $installationState = Get-ExistingInstallationState -Context $context -InstallDirectory $InstallDirectory
    $probeState = Get-InstallationDataProbeResult -ProbeExecutablePath $ProbeExecutablePath -DatabasePath $installationState.DatabasePath -RuntimeConfigPath $installationState.RuntimeConfigPath -LogPath $logPath
    $signalSummary = @(
        "Service=$($installationState.Signals.Service)",
        "ScheduledTask=$($installationState.Signals.ScheduledTask)",
        "InstallFolder=$($installationState.Signals.InstallFolder)",
        "Database=$($installationState.Signals.Database)",
        "RuntimeConfig=$($installationState.Signals.RuntimeConfig)",
        "Registry=$($installationState.Signals.Registry)"
    ) -join ", "
    Write-BootstrapLog ("Installation signals: " + $signalSummary)
    Write-InstallerLog -Message ("نتيجة اكتشاف النسخة السابقة: " + $signalSummary) -LogPath $logPath

    if ($probeState) {
        $probeSummary = @(
            "HasDatabase=$($probeState.HasDatabase)",
            "HasUsers=$($probeState.HasUsers)",
            "UserCount=$($probeState.UserCount)",
            "IsInitialSetupCompleted=$($probeState.IsInitialSetupCompleted)",
            "IsUpgradeCandidate=$($probeState.IsUpgradeCandidate)"
        ) -join ", "
        Write-BootstrapLog ("Install-state probe: " + $probeSummary)
        Write-InstallerLog -Message ("حالة البيانات الفعلية: " + $probeSummary) -LogPath $logPath
    }

    if (-not $installationState.HasAnySignal) {
        Write-BootstrapLog "No prior installation signal detected."
        Write-InstallerLog -Message "لم يتم العثور على نسخة سابقة. سيتم المتابعة كتثبيت جديد." -LogPath $logPath
        exit 0
    }

    $hasUpgradeState = ($probeState -and $probeState.IsUpgradeCandidate) -or $installationState.HasPersistedState
    if ($hasUpgradeState) {
        Write-BootstrapLog "Persisted runtime data detected. Entering safe upgrade preparation."
        Write-InstallerLog -Message "تم اكتشاف قاعدة بيانات أو إعدادات تشغيل سابقة. سيتم تنفيذ الترقية مع الحفاظ على البيانات الحالية." -LogPath $logPath
    }
    else {
        Write-BootstrapLog "Operational install traces detected without persisted runtime data. Entering safe repair preparation."
        Write-InstallerLog -Message "تم اكتشاف بقايا تثبيت سابقة دون بيانات تشغيل مؤكدة. سيتم استبدال الملفات فقط دون حذف ProgramData." -LogPath $logPath
    }

    Stop-ServiceIfExists -ServiceName $context.ServiceName -LogPath $logPath | Out-Null
    if (Get-Command Stop-LegacyServicesIfExists -ErrorAction SilentlyContinue) {
        Stop-LegacyServicesIfExists -Context $context -LogPath $logPath
    }
    Stop-ScheduledTaskIfExists -Context $context -LogPath $logPath | Out-Null
    Unregister-ScheduledTaskIfExists -Context $context -LogPath $logPath
    Stop-ExecutableProcesses -Context $context -LogPath $logPath

    $storageRoot = $installationState.StorageRoot
    $backupsRoot = Join-Path $storageRoot "backups\installer-upgrades"
    New-DirectoryIfMissing -Path $backupsRoot

    if (-not (Test-WriteAccess -Path $backupsRoot)) {
        throw "تعذر الكتابة إلى مسار النسخ الاحتياطي: $backupsRoot"
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupCount = 0

    if ($hasUpgradeState -and (Test-Path $installationState.DatabasePath)) {
        $databaseBackupPath = Join-Path $backupsRoot ("vehicle-permit-system-" + $timestamp + ".db.bak")
        Copy-Item $installationState.DatabasePath $databaseBackupPath -Force
        Write-InstallerLog -Message ("تم أخذ نسخة احتياطية من قاعدة البيانات: " + $databaseBackupPath) -LogPath $logPath
        $backupCount++
    }

    if ($hasUpgradeState -and (Test-Path $installationState.RuntimeConfigPath)) {
        $configBackupPath = Join-Path $backupsRoot ("appsettings.runtime-" + $timestamp + ".json")
        Copy-Item $installationState.RuntimeConfigPath $configBackupPath -Force
        Write-InstallerLog -Message ("تم أخذ نسخة احتياطية من إعدادات التشغيل: " + $configBackupPath) -LogPath $logPath
        $backupCount++
    }

    if ($backupCount -eq 0) {
        Write-InstallerLog -Message "لم يتم العثور على بيانات تشغيل مؤكدة لأخذ نسخة احتياطية. سيستمر التثبيت دون فشل." -LogPath $logPath
    }

    Write-BootstrapLog "prepare-upgrade.ps1 completed successfully."
    Write-InstallerLog -Message "انتهت مرحلة تجهيز التثبيت/الترقية بنجاح." -LogPath $logPath
}
catch {
    $exceptionMessage = $_.Exception.Message
    $scriptStackTrace = $_.ScriptStackTrace
    Write-BootstrapLog ("prepare-upgrade.ps1 failed: " + $exceptionMessage)
    if (-not [string]::IsNullOrWhiteSpace($scriptStackTrace)) {
        Write-BootstrapLog ("Stack: " + $scriptStackTrace)
    }
    if (-not [string]::IsNullOrWhiteSpace($logPath)) {
        Write-InstallerLog -Message ("تحذير أثناء تجهيز التحديث: " + $exceptionMessage + ". سيستمر التحديث.") -Level "WARN" -LogPath $logPath
    }
    exit 0
}
