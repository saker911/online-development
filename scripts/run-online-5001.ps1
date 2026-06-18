param(
    [switch]$NoBuild,
    [switch]$Detached
)

$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectFile = Join-Path $projectRoot "VehiclePermitSystemWeb.csproj"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

$existingProcessIds = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -eq 5001 } |
    Select-Object -ExpandProperty OwningProcess -Unique

foreach ($processId in $existingProcessIds) {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($process) {
        Stop-Process -Id $processId -Force
    }
}

$env:ASPNETCORE_URLS = "http://localhost:5001"
$env:DOTNET_ROOT = $null

$arguments = @("run", "--project", $projectFile, "--urls", "http://localhost:5001")
if ($NoBuild) {
    $arguments += "--no-build"
}

Write-Host "تشغيل نسخة الأون لاين على http://localhost:5001"
if ($Detached) {
    $argumentText = ($arguments | ForEach-Object {
        $argument = $_.ToString()
        if ($argument -match "\s") {
            '"' + $argument.Replace('"', '\"') + '"'
        } else {
            $argument
        }
    }) -join " "

    Start-Process -FilePath $dotnet -ArgumentList $argumentText -WorkingDirectory $projectRoot -WindowStyle Hidden
    Start-Sleep -Seconds 5

    $listener = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -eq 5001 } |
        Select-Object -First 1

    if (-not $listener) {
        throw "لم يتم فتح المنفذ 5001 بعد تشغيل النسخة في الخلفية."
    }

    Write-Host "تم تشغيل النسخة في الخلفية على http://localhost:5001"
    return
}

& $dotnet @arguments
