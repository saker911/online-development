using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Visits;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Visits)]
public sealed class VisitorWorkflowTests
{
    [Fact]
    public void DefaultWorkflowPreservesTheExistingSafeVisitorJourney()
    {
        using var provider = BuildProvider();
        var service = new VisitorWorkflowService(
            provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>()
        );

        var settings = service.GetSettings();

        Assert.True(settings.IsEnabled);
        Assert.Equal(VisitorWorkflowTemplates.Standard, settings.TemplateKey);
        Assert.True(settings.ShowNationalId);
        Assert.False(settings.RequireNationalId);
        Assert.True(settings.RequireHostName);
        Assert.True(settings.RequireVisitLocation);
        Assert.True(settings.RequirePurpose);
        Assert.Equal(5, settings.MinimumLeadMinutes);
        Assert.Equal(30, settings.MaximumAdvanceDays);
    }

    [Fact]
    public void WorkflowSettingsArePersistedAndHiddenFieldsCannotRemainRequired()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var service = new VisitorWorkflowService(factory);
        var model = VisitorWorkflowSettingsViewModel.CreateDefault();
        model.TemplateKey = VisitorWorkflowTemplates.Express;
        model.ShowNationalId = false;
        model.RequireNationalId = true;
        model.ShowVisitLocation = false;
        model.RequireVisitLocation = true;
        model.MinimumLeadMinutes = 0;
        model.WelcomeMessage = "مرحبًا بك في بوابة الزيارة السريعة.";

        service.Update(model);

        var saved = service.GetSettings();
        Assert.Equal(VisitorWorkflowTemplates.Express, saved.TemplateKey);
        Assert.False(saved.ShowNationalId);
        Assert.False(saved.RequireNationalId);
        Assert.False(saved.ShowVisitLocation);
        Assert.False(saved.RequireVisitLocation);
        Assert.Equal(0, saved.MinimumLeadMinutes);
        Assert.Equal("مرحبًا بك في بوابة الزيارة السريعة.", saved.WelcomeMessage);
        using var db = factory.CreateDbContext();
        Assert.Single(db.VisitorWorkflowSettings);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"visitor-workflow-{Guid.NewGuid():N}")
        );
        return services.BuildServiceProvider();
    }
}
