# Test workflow

The solution keeps the complete test run available through:

```powershell
dotnet test
```

## Test areas

The xUnit suite uses the `Area` trait. Run one area with:

```powershell
dotnet test --filter "Area=Authentication"
dotnet test --filter "Area=Permits"
dotnet test --filter "Area=VehiclePermits"
dotnet test --filter "Area=VehicleScan"
dotnet test --filter "Area=Visits"
dotnet test --filter "Area=Attendance"
dotnet test --filter "Area=Administration"
dotnet test --filter "Area=Security"
dotnet test --filter "Area=Database"
dotnet test --filter "Area=Notifications"
```

Combine areas with an OR filter:

```powershell
dotnet test --filter "Area=Permits|Area=VehicleScan"
```

## Tests affected by Git changes

Run the conservative Git-based selector:

```powershell
.\scripts\test-affected.ps1
```

Inspect its decision without running tests:

```powershell
.\scripts\test-affected.ps1 -ListOnly
```

Compare a branch with a base reference:

```powershell
.\scripts\test-affected.ps1 -BaseRef origin/main
```

Run an explicit area through the same script:

```powershell
.\scripts\test-affected.ps1 -Area VehicleScan
```

The selector includes staged, unstaged, and untracked files. Shared infrastructure such as
`Program.cs`, `ApplicationDbContext`, middleware, security, entity models, migrations, shared
services, and shared layout/styles triggers the full .NET and E2E suites. Unknown code paths also
fall back to the full suite.

## E2E

Run all Playwright tests with:

```powershell
npm run e2e
```

The affected-test script selects related Playwright specs for domain changes. Use `-SkipE2E` only
for a quick local .NET iteration. Pull requests and release branches always execute both complete
suites in CI.
