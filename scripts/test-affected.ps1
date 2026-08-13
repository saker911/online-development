[CmdletBinding()]
param(
    [ValidateSet(
        'Authentication',
        'Permits',
        'VehiclePermits',
        'VehicleScan',
        'Visits',
        'Attendance',
        'Administration',
        'Security',
        'Database',
        'Notifications',
        'E2E'
    )]
    [string[]] $Area,

    [string] $BaseRef,

    [switch] $SkipE2E,

    [switch] $ListOnly
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $root 'tests\PermitBehaviorChecks\PermitBehaviorChecks.csproj'

$dotnetAreas = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$e2eSpecs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$reasons = [System.Collections.Generic.List[string]]::new()
$runAllDotnet = $false
$runAllE2E = $false

function Add-Area([string] $Name, [string] $Reason) {
    if ($Name -eq 'E2E') {
        $script:runAllE2E = $true
    }
    elseif ($script:dotnetAreas.Add($Name)) {
        $script:reasons.Add("$Name <= $Reason")
    }
}

function Add-E2E([string[]] $Specs) {
    foreach ($spec in $Specs) {
        [void] $script:e2eSpecs.Add($spec)
    }
}

function Get-ChangedFiles {
    $files = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    if ($BaseRef) {
        git -C $root rev-parse --verify $BaseRef *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Git base reference '$BaseRef' does not exist."
        }

        git -C $root diff --name-only --diff-filter=ACMRTUXB "$BaseRef...HEAD" |
            ForEach-Object { if ($_){ [void] $files.Add($_.Replace('\', '/')) } }
    }

    git -C $root diff --name-only --diff-filter=ACMRTUXB |
        ForEach-Object { if ($_){ [void] $files.Add($_.Replace('\', '/')) } }
    git -C $root diff --cached --name-only --diff-filter=ACMRTUXB |
        ForEach-Object { if ($_){ [void] $files.Add($_.Replace('\', '/')) } }
    git -C $root ls-files --others --exclude-standard |
        ForEach-Object { if ($_){ [void] $files.Add($_.Replace('\', '/')) } }

    return @($files)
}

function Resolve-ChangedFile([string] $Path) {
    $lower = $Path.ToLowerInvariant()

    if ($lower -match '(^|/)(program\.cs|globalusings\.cs|.*\.sln|.*\.csproj|directory\.(build|packages)\..*)$' -or
        $lower -match '^(data/|migrations/|infrastructure/|security/)' -or
        $lower -match '^models/entities/' -or
        $lower -match '^services/(bootstrap|common|audit)/' -or
        $lower -match 'middleware' -or
        $lower -match '^views/shared/' -or
        $lower -match '^wwwroot/(css|icons)/') {
        $script:runAllDotnet = $true
        $script:runAllE2E = $true
        $script:reasons.Add("ALL <= shared/core file: $Path")
        return
    }

    if ($lower -match '^tests/permitbehaviorchecks/(tests/common/|testareas\.cs|permitbehaviorxunittests\.cs|globalusings\.cs)') {
        $script:runAllDotnet = $true
        $script:reasons.Add("ALL <= shared test infrastructure: $Path")
        return
    }

    if ($lower -match '^tests/e2e/(.+\.spec\.js)$') {
        Add-E2E @($Matches[1])
        return
    }

    if ($lower -match '(account|authentication|password|external(login|provider)|useraccountservice)') {
        Add-Area 'Authentication' $Path
        Add-Area 'Security' $Path
        Add-E2E @('auth.spec.js', 'authentication-entry.spec.js', 'password-recovery.spec.js')
        return
    }

    if ($lower -match '(vehiclepermit|vehicle-permit)') {
        Add-Area 'VehiclePermits' $Path
        Add-Area 'VehicleScan' $Path
        Add-E2E @('permits-workflow.spec.js', 'scan-console.spec.js')
        return
    }

    if ($lower -match '(scan|display|gate|barcode|qr)') {
        Add-Area 'VehicleScan' $Path
        Add-Area 'Security' $Path
        Add-E2E @('scan-console.spec.js', 'reports-monitoring.spec.js')
        return
    }

    if ($lower -match '(attendance|movement|leave|workhours|work-hours)') {
        Add-Area 'Attendance' $Path
        Add-Area 'Permits' $Path
        Add-E2E @('reports-monitoring.spec.js', 'reports-layout.spec.js')
        return
    }

    if ($lower -match '(visit|visitor|queue)') {
        Add-Area 'Visits' $Path
        Add-E2E @('visits-workflow.spec.js', 'visitor-workflow.spec.js', 'public-visit-request.spec.js')
        return
    }

    if ($lower -match '(permit)') {
        Add-Area 'Permits' $Path
        Add-E2E @('permits-workflow.spec.js', 'reports-monitoring.spec.js')
        return
    }

    if ($lower -match '(notification|email|outbox)') {
        Add-Area 'Notifications' $Path
        Add-E2E @('notifications.spec.js')
        return
    }

    if ($lower -match '(tenant|administration|department|delegation|subscription|pricing|user|workplace|site)') {
        Add-Area 'Administration' $Path
        Add-Area 'Security' $Path
        Add-E2E @('administration.spec.js', 'tenants-management.spec.js', 'users-permissions.spec.js', 'workplace.spec.js')
        return
    }

    if ($lower -match '^tests/permitbehaviorchecks/') {
        $script:runAllDotnet = $true
        $script:reasons.Add("ALL <= unclassified test file: $Path")
        return
    }

    if ($lower -match '^(docs/|ops/|packaging/|\.github/|.*\.md$|.*\.txt$)') {
        return
    }

    $script:runAllDotnet = $true
    $script:runAllE2E = $true
    $script:reasons.Add("ALL <= unknown impact: $Path")
}

if ($Area) {
    foreach ($selectedArea in $Area) {
        Add-Area $selectedArea 'explicit selection'
    }
}
else {
    $changedFiles = Get-ChangedFiles
    if ($changedFiles.Count -eq 0) {
        $runAllDotnet = $true
        $reasons.Add('ALL <= no Git changes were found')
    }
    else {
        foreach ($file in $changedFiles) {
            Resolve-ChangedFile $file
        }
    }
}

Write-Host ''
Write-Host 'Affected-test decision:' -ForegroundColor Cyan
$reasons | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }

if ($runAllDotnet) {
    Write-Host '  .NET: all tests' -ForegroundColor Yellow
}
else {
    $selected = @($dotnetAreas | Sort-Object)
    if ($selected.Count -eq 0 -and -not $runAllE2E -and $e2eSpecs.Count -eq 0) {
        Write-Host '  No executable-code changes detected.' -ForegroundColor Green
        exit 0
    }
    Write-Host "  .NET areas: $($selected -join ', ')" -ForegroundColor Yellow
}

if (-not $SkipE2E -and ($runAllE2E -or $e2eSpecs.Count -gt 0)) {
    Write-Host $(if ($runAllE2E) { '  E2E: all specs' } else { "  E2E specs: $(@($e2eSpecs | Sort-Object) -join ', ')" }) -ForegroundColor Yellow
}

if ($ListOnly) {
    exit 0
}

Push-Location $root
try {
    if ($runAllDotnet) {
        dotnet test $testProject
    }
    elseif ($dotnetAreas.Count -gt 0) {
        $filter = (@($dotnetAreas | Sort-Object) | ForEach-Object { "Area=$_" }) -join '|'
        dotnet test $testProject --filter $filter
    }

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if (-not $SkipE2E -and $runAllE2E) {
        npm run e2e
    }
    elseif (-not $SkipE2E -and $e2eSpecs.Count -gt 0) {
        dotnet build --configuration Release
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
        $specPaths = @($e2eSpecs | Sort-Object | ForEach-Object { "tests/e2e/$_" })
        & npx playwright test @specPaths
    }

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
