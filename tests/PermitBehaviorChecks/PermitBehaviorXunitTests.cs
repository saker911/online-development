using QuestPDF.Infrastructure;
using Xunit;

namespace PermitBehaviorChecks;

public abstract class ScenarioAreaTestBase
{
    protected static async Task RunAsync(string area)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var scenarios = ScenarioCatalog.GetScenarios(area);
        Assert.NotEmpty(scenarios);
        await TestRunner.RunAllAsync(scenarios);
    }
}

[Trait("Area", TestAreas.Authentication)]
public sealed class AuthenticationScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task AuthenticationScenariosPass() => RunAsync(TestAreas.Authentication);
}

[Trait("Area", TestAreas.Permits)]
public sealed class PermitScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task PermitScenariosPass() => RunAsync(TestAreas.Permits);
}

[Trait("Area", TestAreas.VehiclePermits)]
public sealed class VehiclePermitScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task VehiclePermitScenariosPass() => RunAsync(TestAreas.VehiclePermits);
}

[Trait("Area", TestAreas.VehicleScan)]
public sealed class VehicleScanScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task VehicleScanScenariosPass() => RunAsync(TestAreas.VehicleScan);
}

[Trait("Area", TestAreas.Visits)]
public sealed class VisitScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task VisitScenariosPass() => RunAsync(TestAreas.Visits);
}

[Trait("Area", TestAreas.Attendance)]
public sealed class AttendanceScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task AttendanceScenariosPass() => RunAsync(TestAreas.Attendance);
}

[Trait("Area", TestAreas.Administration)]
public sealed class AdministrationScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task AdministrationScenariosPass() => RunAsync(TestAreas.Administration);
}

[Trait("Area", TestAreas.Security)]
public sealed class SecurityScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task SecurityScenariosPass() => RunAsync(TestAreas.Security);
}

[Trait("Area", TestAreas.Database)]
public sealed class DatabaseScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task DatabaseScenariosPass() => RunAsync(TestAreas.Database);
}

[Trait("Area", TestAreas.Notifications)]
public sealed class NotificationScenarioTests : ScenarioAreaTestBase
{
    [Fact]
    public Task NotificationScenariosPass() => RunAsync(TestAreas.Notifications);
}

public sealed class ScenarioClassificationTests
{
    [Fact]
    public void EveryScenarioBelongsToAKnownArea()
    {
        var scenarios = ScenarioCatalog.GetAllScenarios();
        Assert.Equal(148, scenarios.Count);
        Assert.All(scenarios, scenario => Assert.Contains(scenario.Area, TestAreas.All));
    }
}
