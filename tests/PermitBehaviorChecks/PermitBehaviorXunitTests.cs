using QuestPDF.Infrastructure;
using Xunit;

namespace PermitBehaviorChecks;

public sealed class PermitBehaviorXunitTests
{
    [Fact]
    public async Task AllPermitBehaviorChecksPass()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        await TestRunner.RunAllAsync(ScenarioCatalog.GetAllScenarios());
    }
}
