namespace PermitBehaviorChecks;

internal sealed record TestScenario(string Name, string Area, Func<TestFixture, Task> Execute);
