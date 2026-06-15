param(
    [string]$ServiceName = "VehiclePermitSystem",
    [string]$ServiceDisplayName = "Vehicle Permit System",
    [string]$TaskName = "VehiclePermitSystemStartup",
    [string]$BindAddress = "127.0.0.1",
    [int]$Port = 5000,
    [ValidateSet("SameAsRequest", "Always", "None")]
    [string]$CookieSecurePolicy = "SameAsRequest",
    [switch]$ConfirmHttpsCookieSecurity,
    [switch]$ForceScheduledTask
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "installer-common.ps1")

function Assert-CookieSecurePolicyAllowed {
    param(
        [Parameter(Mandatory = $true)][string]$CookieSecurePolicy,
        [Parameter(Mandatory = $true)][bool]$ConfirmHttpsCookieSecurity
    )

    if ([string]::Equals($CookieSecurePolicy, "Always", [StringComparison]::OrdinalIgnoreCase) -and -not $ConfirmHttpsCookieSecurity) {
        throw "CookieSecurePolicy=Always يتطلب تمرير -ConfirmHttpsCookieSecurity بعد ضبط HTTPS فعليًا. استخدم SameAsRequest للتشغيل الداخلي والتثبيت الأولي."
    }
}

function Resolve-EffectiveCookieSecurePolicy {
    param(
        [string]$ExistingPolicy,
        [Parameter(Mandatory = $true)][string]$RequestedPolicy,
        [Parameter(Mandatory = $true)][bool]$ConfirmHttpsCookieSecurity
    )

    if (
        [string]::Equals($ExistingPolicy, "Always", [StringComparison]::OrdinalIgnoreCase) -and
        -not ([string]::Equals($RequestedPolicy, "Always", [StringComparison]::OrdinalIgnoreCase) -and $ConfirmHttpsCookieSecurity)
    ) {
        return "SameAsRequest"
    }

    Assert-CookieSecurePolicyAllowed -CookieSecurePolicy $RequestedPolicy -ConfirmHttpsCookieSecurity $ConfirmHttpsCookieSecurity
    return $RequestedPolicy
}

function Set-RuntimeConfiguration {
    param(
        [Parameter(Mandatory = $true)][string]$ConfigurationPath,
        [Parameter(Mandatory = $true)][string]$BindAddress,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$CookieSecurePolicy,
        [Parameter(Mandatory = $true)][bool]$ConfirmHttpsCookieSecurity
    )

    if (Test-Path $ConfigurationPath) {
        $runtimeConfig = Get-Content -LiteralPath $ConfigurationPath -Raw | ConvertFrom-Json
        if (-not $runtimeConfig.PSObject.Properties.Name.Contains("Security")) {
            $runtimeConfig | Add-Member -MemberType NoteProperty -Name Security -Value ([pscustomobject]@{})
        }

        $effectivePolicy = Resolve-EffectiveCookieSecurePolicy -ExistingPolicy ([string]$runtimeConfig.Security.CookieSecurePolicy) -RequestedPolicy $CookieSecurePolicy -ConfirmHttpsCookieSecurity $ConfirmHttpsCookieSecurity

        if ($runtimeConfig.Security.PSObject.Properties.Name.Contains("CookieSecurePolicy")) {
            $runtimeConfig.Security.CookieSecurePolicy = $effectivePolicy
        }
        else {
            $runtimeConfig.Security | Add-Member -MemberType NoteProperty -Name CookieSecurePolicy -Value $effectivePolicy
        }

        $runtimeConfig |
        ConvertTo-Json -Depth 6 |
        Set-Content -Path $ConfigurationPath -Encoding UTF8
        return
    }

    Assert-CookieSecurePolicyAllowed -CookieSecurePolicy $CookieSecurePolicy -ConfirmHttpsCookieSecurity $ConfirmHttpsCookieSecurity

    $runtimeConfig = [ordered]@{
        App                     = @{ Urls = "http://$BindAddress`:$Port" }
        Data                    = @{ Provider = "Sqlite" }
        DevelopmentUseLocalData = $false
        Security                = @{ CookieSecurePolicy = $CookieSecurePolicy }
        Backup                  = @{ Enabled = $true; RetentionDays = 30; AutoBackupHour = 2 }
        ConnectionStrings       = @{ SqliteConnection = "Data Source=vehicle-permit-system.db" }
    }

    $runtimeConfig |
    ConvertTo-Json -Depth 6 |
    Set-Content -Path $ConfigurationPath -Encoding UTF8
}

function Invoke-DatabaseUpgrade {
    param(
        [Parameter(Mandatory = $true)][string]$AppPath,
        [string]$LogPath
    )

    [System.Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production", "Process")
    [System.Environment]::SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production", "Process")

    Write-InstallerLog -Message "تشغيل ترقية قاعدة البيانات قبل بدء الخدمة/المهمة." -LogPath $LogPath

    $process = Start-Process -FilePath $AppPath `
        -ArgumentList "--upgrade-db" `
        -PassThru `
        -WindowStyle Hidden

    if (-not $process.WaitForExit(60000)) {
        try {
            $process.Kill()
        }
        catch {
        }

        throw "انتهت مهلة ترقية قاعدة البيانات (60 ثانية)."
    }

    if ($process.ExitCode -ne 0) {
        throw "فشل تنفيذ schema upgrade. رمز الخروج: $($process.ExitCode)"
    }

    Write-InstallerLog -Message "اكتملت ترقية قاعدة البيانات بنجاح." -LogPath $LogPath
}

function Start-ServiceMode {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$AppPath,
        [Parameter(Mandatory = $true)][string]$ServiceDisplayName,
        [Parameter(Mandatory = $true)][string]$ApplicationUrl,
        [string]$LogPath
    )

    $existingService = Get-Service -Name $Context.ServiceName -ErrorAction SilentlyContinue
    $binPath = '"' + $AppPath + '"'
    $displayNameValue = '"' + $ServiceDisplayName + '"'

    if (-not $existingService) {
        Invoke-ScCommand -Arguments @(
            "create",
            $Context.ServiceName,
            "binPath=",
            $binPath,
            "start=",
            "auto",
            "DisplayName=",
            $displayNameValue,
            "obj=",
            "LocalSystem"
        )
        Write-InstallerLog -Message "تم إنشاء خدمة Windows جديدة." -LogPath $LogPath
    }
    else {
        Invoke-ScCommand -Arguments @(
            "config",
            $Context.ServiceName,
            "binPath=",
            $binPath,
            "start=",
            "auto",
            "DisplayName=",
            $displayNameValue
        )
        Write-InstallerLog -Message "تم تحديث إعدادات خدمة Windows الحالية." -LogPath $LogPath
    }

    Invoke-ScCommand -Arguments @(
        "description",
        $Context.ServiceName,
        '"Vehicle Permit System web application service"'
    )
    Invoke-ScCommand -Arguments @(
        "failure",
        $Context.ServiceName,
        "reset= 86400",
        "actions= restart/60000/restart/60000/restart/60000"
    )
    Invoke-ScCommand -Arguments @("failureflag", $Context.ServiceName, "1")

    $serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$($Context.ServiceName)"
    if (-not (Test-Path $serviceRegistryPath)) {
        New-Item -Path $serviceRegistryPath -Force | Out-Null
    }
    New-ItemProperty -Path $serviceRegistryPath -Name "DelayedAutoStart" -Value 1 -PropertyType DWord -Force | Out-Null

    Unregister-ScheduledTaskIfExists -Context $Context -LogPath $LogPath
    Start-Service -Name $Context.ServiceName
    try {
        (Get-Service -Name $Context.ServiceName).WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Running,
            (New-TimeSpan -Seconds 30)
        )
    }
    catch {
    }

    if (-not (Wait-ApplicationReady -Url $ApplicationUrl -TimeoutSeconds 60 -LogPath $LogPath)) {
        throw "تم إنشاء الخدمة ولكن التطبيق لم يستجب على $ApplicationUrl"
    }

    Write-InstallerLog -Message "تم تشغيل النظام باستخدام Windows Service بنجاح." -LogPath $LogPath
}

function Start-ScheduledTaskMode {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$StartScriptPath,
        [Parameter(Mandatory = $true)][string]$BindAddress,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$ApplicationUrl,
        [string]$LogPath
    )

    Remove-ServiceIfExists -ServiceName $Context.ServiceName -LogPath $LogPath
    Register-StartupScheduledTask -Context $Context -ScriptPath $StartScriptPath -BindAddress $BindAddress -Port $Port -LogPath $LogPath
    Start-StartupScheduledTask -Context $Context -LogPath $LogPath

    if (-not (Wait-ApplicationReady -Url $ApplicationUrl -TimeoutSeconds 60 -LogPath $LogPath)) {
        throw "فشل تشغيل التطبيق حتى بعد إنشاء Scheduled Task احتياطية."
    }

    Write-InstallerLog -Message "تم تشغيل النظام باستخدام Scheduled Task احتياطية." -LogPath $LogPath
}

Assert-Administrator

$context = Get-InstallerContext -ServiceName $ServiceName -TaskName $TaskName
$logPath = Get-InstallerLogPath -Context $context
$appRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $appRoot $context.ExecutableName
$startScriptPath = Join-Path $PSScriptRoot "start-background.ps1"

Write-InstallerLog -Message "بدء تثبيت خدمة النظام أو بديل Scheduled Task." -LogPath $logPath

if (-not (Test-Path $appPath)) {
    throw "لم يتم العثور على الملف التنفيذي: $appPath"
}

if (-not (Test-Path $startScriptPath)) {
    throw "لم يتم العثور على سكربت التشغيل الخلفي: $startScriptPath"
}

$storageRoot = Resolve-StorageRoot -Context $context
$configurationRoot = Join-Path $storageRoot "config"
$dataRoot = Join-Path $storageRoot "data"
$uploadsRoot = Join-Path $storageRoot "uploads"
$backupsRoot = Join-Path $storageRoot "backups"
$logsRoot = Join-Path $storageRoot "logs"
$configurationPath = Join-Path $configurationRoot "appsettings.runtime.json"

New-DirectoryIfMissing -Path $storageRoot
New-DirectoryIfMissing -Path $configurationRoot
New-DirectoryIfMissing -Path $dataRoot
New-DirectoryIfMissing -Path $uploadsRoot
New-DirectoryIfMissing -Path $backupsRoot
New-DirectoryIfMissing -Path $logsRoot

if (-not (Test-WriteAccess -Path $storageRoot)) {
    throw "تعذر الكتابة إلى مسار البيانات: $storageRoot"
}

Set-RuntimeConfiguration -ConfigurationPath $configurationPath -BindAddress $BindAddress -Port $Port -CookieSecurePolicy $CookieSecurePolicy -ConfirmHttpsCookieSecurity $ConfirmHttpsCookieSecurity.IsPresent
$Port = Resolve-ConfiguredPort -RuntimeConfigPath $configurationPath -FallbackPort $Port
$applicationUrl = "http://localhost:$Port"
$runtimeState = Get-DotNetRuntimeState
Write-InstallerLog -Message ("حالة .NET Runtime: dotnet=" + $runtimeState.DotNetAvailable + ", aspnetcore8=" + $runtimeState.AspNetCore8Installed + ", windowsdesktop8=" + $runtimeState.WindowsDesktop8Installed + ". " + $runtimeState.Notes) -LogPath $logPath

Stop-ServiceIfExists -ServiceName $context.ServiceName -LogPath $logPath | Out-Null
Stop-LegacyServicesIfExists -Context $context -LogPath $logPath
Stop-ScheduledTaskIfExists -Context $context -LogPath $logPath | Out-Null
Stop-ExecutableProcesses -Context $context -LogPath $logPath
Invoke-DatabaseUpgrade -AppPath $appPath -LogPath $logPath

try {
    if ($ForceScheduledTask) {
        throw "تم طلب استخدام Scheduled Task مباشرة."
    }

    Start-ServiceMode -Context $context -AppPath $appPath -ServiceDisplayName $ServiceDisplayName -ApplicationUrl $applicationUrl -LogPath $logPath
}
catch {
    Write-InstallerLog -Message ("فشل تشغيل Windows Service: " + $_.Exception.Message) -Level "WARN" -LogPath $logPath
    Start-ScheduledTaskMode -Context $context -StartScriptPath $startScriptPath -BindAddress $BindAddress -Port $Port -ApplicationUrl $applicationUrl -LogPath $logPath
}

Write-InstallerLog -Message ("مسار البرنامج: " + $appRoot) -LogPath $logPath
Write-InstallerLog -Message ("مسار البيانات: " + $storageRoot) -LogPath $logPath
Write-InstallerLog -Message ("الرابط المحلي: " + $applicationUrl) -LogPath $logPath
