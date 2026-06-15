namespace PermitBehaviorChecks;

internal sealed record TestScenario(string Name, Func<TestFixture, Task> Execute);
