param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot "VehiclePermitSystemWeb.csproj"

if (-not (Test-Path $projectFile)) {
    throw "Project file not found: $projectFile"
}

Write-Host "Running validation gate for VehiclePermitSystemWeb..." -ForegroundColor Cyan

dotnet restore $projectFile
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

dotnet build $projectFile -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

$testProject = Join-Path $projectRoot "tests\PermitBehaviorChecks\PermitBehaviorChecks.csproj"

if (Test-Path $testProject) {
    dotnet test $testProject -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Validation gate completed successfully." -ForegroundColor Green
