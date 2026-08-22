using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using VehiclePermitSystemWeb.Services.Bootstrap;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Security)]
[Trait("Area", TestAreas.Authentication)]
public sealed class InitialSetupAccessPolicyTests
{
    [Fact]
    public void ProductionNeverAllowsWebInitialSetup()
    {
        var policy = BuildPolicy(Environments.Production, allowWebSetup: true);

        Assert.False(policy.IsWebSetupAllowed);
    }

    [Fact]
    public void DevelopmentAllowsWebInitialSetupByDefault()
    {
        var policy = BuildPolicy(Environments.Development, allowWebSetup: null);

        Assert.True(policy.IsWebSetupAllowed);
    }

    [Fact]
    public void DevelopmentWebInitialSetupCanBeDisabled()
    {
        var policy = BuildPolicy(Environments.Development, allowWebSetup: false);

        Assert.False(policy.IsWebSetupAllowed);
    }

    private static InitialSetupAccessPolicy BuildPolicy(
        string environmentName,
        bool? allowWebSetup
    )
    {
        var values = new Dictionary<string, string?>();
        if (allowWebSetup.HasValue)
        {
            values["Bootstrap:AllowWebInitialSetup"] = allowWebSetup.Value.ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new InitialSetupAccessPolicy(
            new TestHostEnvironment { EnvironmentName = environmentName },
            configuration
        );
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PermitBehaviorChecks";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
