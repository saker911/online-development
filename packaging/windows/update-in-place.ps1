param(
    [Parameter(Mandatory = $true)][string]$PackageZip,
    [string]$ServiceName = "VehiclePermitSystem",
    [string]$TaskName = "VehiclePermitSystemStartup"
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "installer-common.ps1")

Assert-Administrator

$context = Get-InstallerContext -ServiceName $ServiceName -TaskName $TaskName
$logPath = Get-InstallerLogPath -Context $context
$appRoot = Split-Path -Parent $PSScriptRoot
$tempRoot = Join-Path $env:TEMP ("VehiclePermitSystemUpdate-" + [Guid]::NewGuid().ToString("N"))

if (-not (Test-Path $PackageZip)) {
    throw "لم يتم العثور على ملف التحديث: $PackageZip"
}

Write-InstallerLog -Message ("بدء تحديث مباشر من الحزمة: " + $PackageZip) -LogPath $logPath

try {
    $prepareUpgradeScript = Join-Path $PSScriptRoot "prepare-upgrade.ps1"
    if (Test-Path $prepareUpgradeScript) {
        try {
            & $prepareUpgradeScript -ServiceName $ServiceName -TaskName $TaskName -InstallDirectory $appRoot
        }
        catch {
            Write-InstallerLog -Message ("فشل prepare-upgrade قبل التحديث: " + $_.Exception.Message + ". سيستمر التحديث.") -Level "WARN" -LogPath $logPath
        }
    }

    New-DirectoryIfMissing -Path $tempRoot
    Expand-Archive -Path $PackageZip -DestinationPath $tempRoot -Force

    if (Test-Path (Join-Path $tempRoot "app\VehiclePermitSystemWeb.exe")) {
        $expandedRoot = Join-Path $tempRoot "app"
    }
    elseif (Test-Path (Join-Path $tempRoot "VehiclePermitSystemWeb.exe")) {
        $expandedRoot = $tempRoot
    }
    else {
        $candidate = Get-ChildItem -Path $tempRoot -Directory | Select-Object -First 1
        if ($candidate -and (Test-Path (Join-Path $candidate.FullName "VehiclePermitSystemWeb.exe"))) {
            $expandedRoot = $candidate.FullName
        }
        else {
            throw "تعذر قراءة محتويات الحزمة المضغوطة."
        }
    }

    robocopy $expandedRoot $appRoot /MIR /XD logs | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "فشل نسخ ملفات التحديث إلى مسار التطبيق. رمز الخروج: $LASTEXITCODE"
    }

    $installServiceScript = Join-Path $appRoot "tools\install-service.ps1"
    if (-not (Test-Path $installServiceScript)) {
        throw "لم يتم العثور على سكربت تثبيت الخدمة بعد نسخ التحديث: $installServiceScript"
    }

    & $installServiceScript -ServiceName $ServiceName -TaskName $TaskName

    Write-InstallerLog -Message "اكتمل التحديث المباشر بنجاح مع الاحتفاظ بالبيانات الخارجية." -LogPath $logPath
}
catch {
    Write-InstallerLog -Message ("فشل التحديث المباشر: " + $_.Exception.Message) -Level "ERROR" -LogPath $logPath
    throw
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
