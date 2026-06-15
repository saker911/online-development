using QuestPDF.Infrastructure;

namespace PermitBehaviorChecks;

internal static class Program
{
    private static async Task Main()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        await TestRunner.RunAllAsync(ScenarioCatalog.GetAllScenarios());
    }
}
