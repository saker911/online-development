param(
    [string]$Version = "1.0.15",
    [string]$BindAddress = "127.0.0.1",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

function Invoke-WithRetry {
    param(
        [scriptblock]$Action,
        [int]$MaxAttempts = 3,
        [int]$DelayMilliseconds = 1000,
        [string]$OperationName = "operation"
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            & $Action
            return
        }
        catch {
            if ($attempt -eq $MaxAttempts) {
                throw
            }

            Write-Warning ("Retrying {0} after failure on attempt {1}: {2}" -f $OperationName, $attempt, $_.Exception.Message)
            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }
}

function Wait-FileReady {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Path $Path)) {
            Start-Sleep -Seconds 1
            continue
        }

        try {
            $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
            $stream.Dispose()
            return
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }

    throw "Timed out waiting for file to become available: $Path"
}

function Copy-DirectorySnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Remove-Item -Path $Destination -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    robocopy $Source $Destination /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "Snapshot copy failed from $Source to $Destination. Exit code: $LASTEXITCODE"
    }
}

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$publishRoot = Join-Path $projectRoot "publish\Releases"
$stagingRoot = Join-Path $publishRoot ("VehiclePermitSystem-" + $Version + "-win-x64")
$zipPath = $stagingRoot + ".zip"
$publishOutput = Join-Path $stagingRoot "app"
$toolsOutput = Join-Path $publishOutput "tools"
$archiveSnapshot = Join-Path $env:TEMP ("VehiclePermitSystemArchive-" + [Guid]::NewGuid().ToString("N"))

Remove-Item -Path $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $zipPath -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $toolsOutput -Force | Out-Null

$fileVersion = "$Version.0"

dotnet publish "$projectRoot\VehiclePermitSystemWeb.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:IncludeAllContentForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false /p:Version=$Version /p:InformationalVersion=$Version /p:FileVersion=$fileVersion /p:AssemblyVersion=$fileVersion -o $publishOutput

Remove-Item (Join-Path $publishOutput "publish") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $publishOutput "wwwroot\uploads") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $publishOutput "appsettings.Development.json") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $publishOutput "appsettings.Local.json") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $publishOutput "package.json") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $publishOutput "package-lock.json") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $publishOutput "test-results") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $publishOutput "security-audit") -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $publishOutput -Recurse -Include *.bak, *.bak-* -File -ErrorAction SilentlyContinue |
Remove-Item -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $PSScriptRoot "start-background.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "ensure-background.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "run-diagnostic.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "open-local.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "stop-background.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "installer-common.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "install-service.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "uninstall-service.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "install-startup.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "uninstall-startup.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "prepare-upgrade.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "update-in-place.ps1") $toolsOutput -Force
Copy-Item (Join-Path $PSScriptRoot "README-AR.md") (Join-Path $publishOutput "README-AR.md") -Force

$versionInfo = [ordered]@{
    version          = $Version
    bindAddress      = $BindAddress
    port             = $Port
    packageCreatedAt = (Get-Date).ToString("s")
    installDirectory = "%ProgramFiles%\\Vehicle Permit System"
    dataDirectory    = "%ProgramData%\\Vehicle Permit System"
    serviceName      = "VehiclePermitSystem"
    taskName         = "VehiclePermitSystemStartup"
    executableName   = "VehiclePermitSystemWeb.exe"
    selfContained    = $true
}

$versionInfo | ConvertTo-Json | Set-Content -Path (Join-Path $publishOutput "version.json") -Encoding UTF8

Wait-FileReady -Path (Join-Path $publishOutput "VehiclePermitSystemWeb.exe")
Copy-DirectorySnapshot -Source $publishOutput -Destination $archiveSnapshot

Invoke-WithRetry -OperationName "ZIP creation" -Action {
    Compress-Archive -Path (Join-Path $archiveSnapshot "*") -DestinationPath $zipPath -Force -ErrorAction Stop
}

Remove-Item -Path $archiveSnapshot -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Package created:" -ForegroundColor Green
Write-Host $zipPath -ForegroundColor Cyan
