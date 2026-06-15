namespace PermitBehaviorChecks;

internal static class TestRunner
{
    public static async Task RunAllAsync(IReadOnlyList<TestScenario> scenarios)
    {
        foreach (var scenario in scenarios)
        {
            await using var fixture = new TestFixture();

            try
            {
                await scenario.Execute(fixture);
                Console.WriteLine($"PASS: {scenario.Name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {scenario.Name}");
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 1;
                throw;
            }
        }

        Console.WriteLine("All permit behavior checks passed.");
    }
}
