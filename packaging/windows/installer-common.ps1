Set-StrictMode -Version Latest

function Get-InstallerContext {
    param(
        [string]$ServiceName = "VehiclePermitSystem",
        [string]$TaskName = "VehiclePermitSystemStartup",
        [string[]]$LegacyTaskNames = @("VehiclePermitSystemWeb", "VehiclePermitSystemService"),
        [string]$ProductFolderName = "Vehicle Permit System",
        [string]$LegacyProductFolderName = "VehiclePermitSystemWeb",
        [string]$ExecutableName = "VehiclePermitSystemWeb.exe"
    )

    $commonAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
    if ([string]::IsNullOrWhiteSpace($commonAppData)) {
        $commonAppData = $env:ProgramData
    }

    $programFiles = if (-not [string]::IsNullOrWhiteSpace($env:ProgramW6432)) {
        $env:ProgramW6432
    }
    else {
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    }

    $taskNames = @($TaskName) + $LegacyTaskNames | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    } | Select-Object -Unique

    $primaryStorageRoot = Join-Path $commonAppData $ProductFolderName
    $legacyStorageRoot = Join-Path $commonAppData $LegacyProductFolderName
    $defaultInstallRoot = if ([string]::IsNullOrWhiteSpace($programFiles)) {
        Join-Path ${env:ProgramFiles} $ProductFolderName
    }
    else {
        Join-Path $programFiles $ProductFolderName
    }

    [pscustomobject]@{
        ServiceName                = $ServiceName
        TaskName                   = $TaskName
        KnownTaskNames             = $taskNames
        ProductFolderName          = $ProductFolderName
        LegacyProductFolderName    = $LegacyProductFolderName
        ExecutableName             = $ExecutableName
        ExecutableProcessName      = [IO.Path]::GetFileNameWithoutExtension($ExecutableName)
        CommonAppData              = $commonAppData
        ProgramFiles               = $programFiles
        PrimaryStorageRoot         = $primaryStorageRoot
        LegacyStorageRoot          = $legacyStorageRoot
        StorageRootCandidates      = @($primaryStorageRoot, $legacyStorageRoot)
        LegacyServiceNames         = @("VehiclePermitSystemService")
        DefaultInstallDirectory    = $defaultInstallRoot
        InstallDirectoryCandidates = @($defaultInstallRoot)
        RuntimeConfigRelativePath  = "config\appsettings.runtime.json"
        DatabaseRelativePath       = "data\vehicle-permit-system.db"
    }
}

function Assert-Administrator {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "يجب تشغيل هذا السكربت كمسؤول."
    }
}

function New-DirectoryIfMissing {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Test-WriteAccess {
    param([Parameter(Mandatory = $true)][string]$Path)

    New-DirectoryIfMissing -Path $Path
    $probePath = Join-Path $Path (".__write-test-" + [Guid]::NewGuid().ToString("N") + ".tmp")
    try {
        Set-Content -Path $probePath -Value "ok" -Encoding ASCII
        Remove-Item -Path $probePath -Force -ErrorAction SilentlyContinue
        return $true
    }
    catch {
        return $false
    }
}

function Resolve-StorageRoot {
    param([Parameter(Mandatory = $true)]$Context)

    foreach ($candidate in $Context.StorageRootCandidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $Context.PrimaryStorageRoot
}

function Get-InstallerLogPath {
    param([Parameter(Mandatory = $true)]$Context)

    $storageRoot = Resolve-StorageRoot -Context $Context
    $logsRoot = Join-Path $storageRoot "logs"
    New-DirectoryIfMissing -Path $storageRoot
    New-DirectoryIfMissing -Path $logsRoot
    return Join-Path $logsRoot "installer.log"
}

function Write-InstallerLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$Level = "INFO",
        [string]$LogPath
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        $logDirectory = Split-Path -Parent $LogPath
        if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
            New-DirectoryIfMissing -Path $logDirectory
        }

        Add-Content -Path $LogPath -Value $entry -Encoding UTF8
    }

    Write-Host $entry
}

function Copy-DirectorySafely {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path $Source)) {
        return
    }

    New-DirectoryIfMissing -Path $Destination
    robocopy $Source $Destination /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "فشل نسخ البيانات من $Source إلى $Destination. رمز الخروج: $LASTEXITCODE"
    }
}

function Resolve-ConfiguredPort {
    param(
        [string]$RuntimeConfigPath,
        [int]$FallbackPort
    )

    if (-not [string]::IsNullOrWhiteSpace($RuntimeConfigPath) -and (Test-Path $RuntimeConfigPath)) {
        try {
            $runtimeConfig = Get-Content -LiteralPath $RuntimeConfigPath -Raw | ConvertFrom-Json
            $configuredUrls = [string]$runtimeConfig.App.Urls
            if (-not [string]::IsNullOrWhiteSpace($configuredUrls)) {
                $firstUrl = ($configuredUrls -split ';' | Select-Object -First 1).Trim()
                if ($firstUrl -match ':([0-9]{2,5})(?:/|$)') {
                    return [int]$Matches[1]
                }
            }
        }
        catch {
        }
    }

    return $FallbackPort
}

function Resolve-ConfiguredUrl {
    param(
        [string]$RuntimeConfigPath,
        [string]$BindAddress,
        [int]$Port
    )

    if (-not [string]::IsNullOrWhiteSpace($RuntimeConfigPath) -and (Test-Path $RuntimeConfigPath)) {
        try {
            $runtimeConfig = Get-Content -LiteralPath $RuntimeConfigPath -Raw | ConvertFrom-Json
            $configuredUrls = [string]$runtimeConfig.App.Urls
            if (-not [string]::IsNullOrWhiteSpace($configuredUrls)) {
                return ($configuredUrls -split ';' | Select-Object -First 1).Trim()
            }
        }
        catch {
        }
    }

    return "http://$BindAddress`:$Port"
}

function Test-PortInUse {
    param([int]$CheckPort)

    try {
        $listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
        return $listeners.Port -contains $CheckPort
    }
    catch {
        return $false
    }
}

function Get-PortOwners {
    param([int]$CheckPort)

    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
        return @()
    }

    Get-NetTCPConnection -LocalPort $CheckPort -State Listen -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object {
        Get-Process -Id $_ -ErrorAction SilentlyContinue
    } |
    Where-Object { $_ -ne $null }
}

function Get-ExecutableProcesses {
    param([Parameter(Mandatory = $true)]$Context)

    Get-Process -Name $Context.ExecutableProcessName -ErrorAction SilentlyContinue
}

function Wait-ForProcessExit {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-ExecutableProcesses -Context $Context)) {
            return $true
        }

        Start-Sleep -Seconds 1
    }

    return -not (Get-ExecutableProcesses -Context $Context)
}

function Stop-ExecutableProcesses {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [string]$LogPath
    )

    $processes = Get-ExecutableProcesses -Context $Context
    if (-not $processes) {
        Write-InstallerLog -Message "لم يتم العثور على عملية تشغيل حالية للتطبيق." -LogPath $LogPath
        return
    }

    foreach ($process in $processes) {
        Write-InstallerLog -Message ("إيقاف العملية {0} ({1})." -f $process.ProcessName, $process.Id) -LogPath $LogPath
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not (Wait-ForProcessExit -Context $Context -TimeoutSeconds 30)) {
        throw "تعذر إيقاف عملية التطبيق خلال المهلة المحددة."
    }
}

function Stop-ServiceIfExists {
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [string]$LogPath
    )

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-InstallerLog -Message "الخدمة غير موجودة، سيتم تجاوز إيقافها." -LogPath $LogPath
        return $false
    }

    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-InstallerLog -Message "الخدمة موجودة لكنها متوقفة بالفعل." -LogPath $LogPath
        return $true
    }

    Write-InstallerLog -Message "إيقاف خدمة Windows الحالية." -LogPath $LogPath
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    try {
        (Get-Service -Name $ServiceName).WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Stopped,
            (New-TimeSpan -Seconds 30)
        )
    }
    catch {
    }

    return $true
}

function Remove-ServiceIfExists {
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [string]$LogPath
    )

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-InstallerLog -Message "الخدمة غير موجودة، لن يتم حذف أي خدمة." -LogPath $LogPath
        return
    }

    Stop-ServiceIfExists -ServiceName $ServiceName -LogPath $LogPath | Out-Null
    & sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "فشل حذف الخدمة $ServiceName. رمز الخروج: $LASTEXITCODE"
    }

    Write-InstallerLog -Message "تم حذف خدمة Windows الحالية." -LogPath $LogPath
}

function Stop-LegacyServicesIfExists {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [string]$LogPath
    )

    $legacyServiceNamesProperty = $Context.PSObject.Properties["LegacyServiceNames"]
    if (-not $legacyServiceNamesProperty) {
        return
    }

    foreach ($legacyServiceName in $legacyServiceNamesProperty.Value) {
        if ([string]::IsNullOrWhiteSpace($legacyServiceName) -or $legacyServiceName -eq $Context.ServiceName) {
            continue
        }

        Stop-ServiceIfExists -ServiceName $legacyServiceName -LogPath $LogPath | Out-Null
    }
}

function Test-ScheduledTasksSupported {
    return [bool](Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue)
}

function Get-RegisteredStartupTask {
    param([Parameter(Mandatory = $true)]$Context)

    if (-not (Test-ScheduledTasksSupported)) {
        return $null
    }

    foreach ($taskName in $Context.KnownTaskNames) {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($task) {
            return $task
        }
    }

    return $null
}

function Stop-ScheduledTaskIfExists {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [string]$LogPath
    )

    $task = Get-RegisteredStartupTask -Context $Context
    if (-not $task) {
        Write-InstallerLog -Message "لا توجد Scheduled Task حالية للتطبيق." -LogPath $LogPath
        return $false
    }

    Write-InstallerLog -Message ("إيقاف Scheduled Task الحالية: {0}." -f $task.TaskName) -LogPath $LogPath
    Stop-ScheduledTask -TaskName $task.TaskName -ErrorAction SilentlyContinue
    return $true
}

function Unregister-ScheduledTaskIfExists {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [string]$LogPath
    )

    $task = Get-RegisteredStartupTask -Context $Context
    if (-not $task) {
        Write-InstallerLog -Message "لا توجد Scheduled Task لإزالتها." -LogPath $LogPath
        return
    }

    try {
        Stop-ScheduledTaskIfExists -Context $Context -LogPath $LogPath | Out-Null
        Unregister-ScheduledTask -TaskName $task.TaskName -Confirm:$false -ErrorAction Stop
        Write-InstallerLog -Message ("تمت إزالة Scheduled Task: {0}." -f $task.TaskName) -LogPath $LogPath
    }
    catch {
        throw "فشل حذف Scheduled Task $($task.TaskName): $($_.Exception.Message)"
    }
}

function Find-UninstallEntry {
    param([Parameter(Mandatory = $true)]$Context)

    $roots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    foreach ($root in $roots) {
        $entries = Get-ItemProperty -Path $root -ErrorAction SilentlyContinue
        foreach ($entry in $entries) {
            $displayNameProperty = $entry.PSObject.Properties["DisplayName"]
            $installLocationProperty = $entry.PSObject.Properties["InstallLocation"]
            $displayName = if ($displayNameProperty) { [string]$displayNameProperty.Value } else { "" }
            $installLocation = if ($installLocationProperty) { [string]$installLocationProperty.Value } else { "" }
            $matchesName = $displayName -like "*Vehicle Permit System*" -or $displayName -like "*تصاريح المركبات*"
            $matchesInstallLocation = -not [string]::IsNullOrWhiteSpace($installLocation) -and $installLocation -like "*Vehicle Permit System*"
            if ($matchesName -or $matchesInstallLocation) {
                return $entry
            }
        }
    }

    return $null
}

function Test-InstallDirectorySignal {
    param(
        [string]$Path,
        [Parameter(Mandatory = $true)]$Context
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return $false
    }

    $expectedFiles = @(
        (Join-Path $Path $Context.ExecutableName),
        (Join-Path $Path 'unins000.exe'),
        (Join-Path $Path 'version.json')
    )

    return [bool]($expectedFiles | Where-Object { Test-Path $_ } | Select-Object -First 1)
}

function Test-UninstallEntrySignal {
    param([object]$Entry)

    if (-not $Entry) {
        return $false
    }

    $uninstallStringProperty = $Entry.PSObject.Properties['UninstallString']
    $installLocationProperty = $Entry.PSObject.Properties['InstallLocation']
    $uninstallString = if ($uninstallStringProperty) { [string]$uninstallStringProperty.Value } else { '' }
    $installLocation = if ($installLocationProperty) { [string]$installLocationProperty.Value } else { '' }

    if (-not [string]::IsNullOrWhiteSpace($installLocation) -and (Test-Path $installLocation)) {
        return $true
    }

    return -not [string]::IsNullOrWhiteSpace($uninstallString)
}

function Get-ExistingInstallationState {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [string]$InstallDirectory
    )

    $storageRoot = Resolve-StorageRoot -Context $Context
    $databasePath = Join-Path $storageRoot $Context.DatabaseRelativePath
    $runtimeConfigPath = Join-Path $storageRoot $Context.RuntimeConfigRelativePath
    $service = Get-Service -Name $Context.ServiceName -ErrorAction SilentlyContinue
    $task = Get-RegisteredStartupTask -Context $Context
    $installCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($InstallDirectory)) {
        $installCandidates += $InstallDirectory
    }
    $installCandidates += $Context.InstallDirectoryCandidates
    $installDirectoryMatch = $installCandidates |
    Where-Object { Test-InstallDirectorySignal -Path $_ -Context $Context } |
    Select-Object -First 1
    $uninstallEntry = Find-UninstallEntry -Context $Context

    $signals = [ordered]@{
        Service       = [bool]$service
        ScheduledTask = [bool]$task
        InstallFolder = -not [string]::IsNullOrWhiteSpace($installDirectoryMatch)
        Database      = Test-Path $databasePath
        RuntimeConfig = Test-Path $runtimeConfigPath
        Registry      = Test-UninstallEntrySignal -Entry $uninstallEntry
    }

    [pscustomobject]@{
        HasAnySignal      = ($signals.Values | Where-Object { $_ } | Measure-Object).Count -gt 0
        HasPersistedState = [bool]($signals.Database -or $signals.RuntimeConfig)
        Signals           = [pscustomobject]$signals
        Service           = $service
        ScheduledTask     = $task
        InstallDirectory  = $installDirectoryMatch
        StorageRoot       = $storageRoot
        DatabasePath      = $databasePath
        RuntimeConfigPath = $runtimeConfigPath
        UninstallEntry    = $uninstallEntry
    }
}

function Get-InstallationDataProbeResult {
    param(
        [string]$ProbeExecutablePath,
        [string]$DatabasePath,
        [string]$RuntimeConfigPath,
        [string]$LogPath
    )

    if ([string]::IsNullOrWhiteSpace($ProbeExecutablePath) -or -not (Test-Path $ProbeExecutablePath)) {
        Write-InstallerLog -Message "لم يتم العثور على أداة فحص حالة البيانات الخاصة بالمثبت. سيتم استخدام إشارات التخزين فقط." -Level "WARN" -LogPath $LogPath
        return $null
    }

    $probeOutputPath = Join-Path ([IO.Path]::GetTempPath()) ("vps-install-state-" + [Guid]::NewGuid().ToString("N") + ".json")

    try {
        $arguments = @(
            "--probe-install-state",
            "--database-path", $DatabasePath,
            "--runtime-config-path", $RuntimeConfigPath
        )
        $process = Start-Process -FilePath $ProbeExecutablePath -ArgumentList $arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $probeOutputPath
        if ($process.ExitCode -ne 0) {
            Write-InstallerLog -Message ("أداة فحص حالة البيانات أعادت رمز خروج غير ناجح: " + $process.ExitCode) -Level "WARN" -LogPath $LogPath
            return $null
        }

        if (-not (Test-Path $probeOutputPath)) {
            Write-InstallerLog -Message "لم تنتج أداة فحص حالة البيانات أي خرج يمكن قراءته." -Level "WARN" -LogPath $LogPath
            return $null
        }

        $rawResult = Get-Content -LiteralPath $probeOutputPath -Raw
        if ([string]::IsNullOrWhiteSpace($rawResult)) {
            Write-InstallerLog -Message "خرج أداة فحص حالة البيانات كان فارغًا." -Level "WARN" -LogPath $LogPath
            return $null
        }

        return $rawResult | ConvertFrom-Json
    }
    catch {
        Write-InstallerLog -Message ("تعذر قراءة حالة البيانات الفعلية من قاعدة النظام: " + $_.Exception.Message) -Level "WARN" -LogPath $LogPath
        return $null
    }
    finally {
        if (Test-Path $probeOutputPath) {
            Remove-Item -LiteralPath $probeOutputPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-DotNetRuntimeState {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        return [pscustomobject]@{
            DotNetAvailable          = $false
            AspNetCore8Installed     = $false
            WindowsDesktop8Installed = $false
            Notes                    = "dotnet.exe غير متوفر. هذا لا يمنع التشغيل لأن الحزمة self-contained."
        }
    }

    $runtimes = & $dotnetCommand.Source --list-runtimes 2>$null
    $aspNetCore8Installed = [bool]($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App\s+8\.' })
    $windowsDesktop8Installed = [bool]($runtimes | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App\s+8\.' })

    return [pscustomobject]@{
        DotNetAvailable          = $true
        AspNetCore8Installed     = $aspNetCore8Installed
        WindowsDesktop8Installed = $windowsDesktop8Installed
        Notes                    = "تم تسجيل حالة Runtime لأغراض التشخيص فقط."
    }
}

function Invoke-ScCommand {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & sc.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "فشل تنفيذ sc.exe $($Arguments -join ' '). رمز الخروج: $LASTEXITCODE"
    }
}

function Register-StartupScheduledTask {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$BindAddress,
        [Parameter(Mandatory = $true)][int]$Port,
        [string]$LogPath
    )

    if (-not (Test-ScheduledTasksSupported)) {
        throw "أوامر Scheduled Task غير متوفرة على هذا الجهاز."
    }

    if (-not (Test-Path $ScriptPath)) {
        throw "لم يتم العثور على سكربت تشغيل الخلفية: $ScriptPath"
    }

    Unregister-ScheduledTaskIfExists -Context $Context -LogPath $LogPath

    $escapedScriptPath = $ScriptPath.Replace('"', '""')
    $arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$escapedScriptPath`" -BindAddress `"$BindAddress`" -Port $Port"
    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew

    Register-ScheduledTask -TaskName $Context.TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description "Vehicle Permit System fallback startup task" -Force | Out-Null
    Write-InstallerLog -Message ("تم إنشاء Scheduled Task احتياطية: {0}." -f $Context.TaskName) -LogPath $LogPath
}

function Start-StartupScheduledTask {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [string]$LogPath
    )

    $task = Get-RegisteredStartupTask -Context $Context
    if (-not $task) {
        throw "لا توجد Scheduled Task لبدء التطبيق."
    }

    Start-ScheduledTask -TaskName $task.TaskName -ErrorAction Stop
    Write-InstallerLog -Message ("تم تشغيل Scheduled Task: {0}." -f $task.TaskName) -LogPath $LogPath
}

function Wait-ApplicationReady {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$TimeoutSeconds = 60,
        [string]$LogPath
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -Method Get -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-InstallerLog -Message "استجابة التطبيق أصبحت جاهزة على $Url." -LogPath $LogPath
                return $true
            }
        }
        catch {
            $exceptionResponse = $_.Exception.Response
            if ($exceptionResponse -and [int]$exceptionResponse.StatusCode -lt 500) {
                Write-InstallerLog -Message "التطبيق استجاب على $Url مع حالة HTTP $([int]$exceptionResponse.StatusCode)." -LogPath $LogPath
                return $true
            }
        }

        Start-Sleep -Seconds 2
    }

    Write-InstallerLog -Message "انتهت المهلة قبل أن يصبح التطبيق جاهزًا." -Level "ERROR" -LogPath $LogPath
    return $false
}
