using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Infrastructure;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;
using VehiclePermitSystemWeb.Services.Tenants;
using Xunit;

namespace PermitBehaviorChecks;

public sealed class TenantFeatureTests
{
    [Fact]
    public void TenantCreationNormalizesDependentServices()
    {
        using var provider = CreateProvider("tenant-feature-create");
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var service = new TenantManagementService(factory, new EphemeralDataProtectionProvider());

        var result = service.CreateTenant(
            new TenantEditorViewModel
            {
                TenantId = "limited-services",
                Name = "جهة خدمات محدودة",
                Slug = "limited-services",
                PermitsServiceEnabled = true,
                VisitsServiceEnabled = false,
                SelfServiceEnabled = true,
                QueueServiceEnabled = true,
                GateServiceEnabled = true,
            }
        );

        Assert.True(result.Succeeded, result.Message);
        using var db = factory.CreateDbContext();
        var tenant = db.Tenants.IgnoreQueryFilters().Single(x => x.TenantId == "limited-services");
        Assert.True(tenant.PermitsServiceEnabled);
        Assert.False(tenant.VisitsServiceEnabled);
        Assert.False(tenant.SelfServiceEnabled);
        Assert.False(tenant.QueueServiceEnabled);
        Assert.True(tenant.GateServiceEnabled);
    }

    [Fact]
    public void FeatureAvailabilityIsTenantScopedAndAppliesDependencies()
    {
        using var provider = CreateProvider("tenant-feature-scope");
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            db.Tenants.AddRange(
                new Tenant
                {
                    TenantId = "full-tenant",
                    Name = "جهة كاملة",
                    Slug = "full-tenant",
                    PermitsServiceEnabled = true,
                    VisitsServiceEnabled = true,
                    SelfServiceEnabled = true,
                    QueueServiceEnabled = true,
                    GateServiceEnabled = true,
                },
                new Tenant
                {
                    TenantId = "visits-off",
                    Name = "جهة بلا زيارات",
                    Slug = "visits-off",
                    PermitsServiceEnabled = true,
                    VisitsServiceEnabled = false,
                    SelfServiceEnabled = true,
                    QueueServiceEnabled = true,
                    GateServiceEnabled = false,
                }
            );
            db.SaveChanges();
        }

        var service = new TenantFeatureService(
            factory,
            provider.GetRequiredService<ITenantContext>()
        );
        var full = service.GetForTenant("full-tenant");
        var restricted = service.GetForTenant("visits-off");

        Assert.True(full.Permits && full.Visits && full.SelfService && full.Queue && full.Gate);
        Assert.True(restricted.Permits);
        Assert.False(restricted.Visits);
        Assert.False(restricted.SelfService);
        Assert.False(restricted.Queue);
        Assert.False(restricted.Gate);
        Assert.False(service.GetForTenant("missing-tenant").Permits);
    }

    [Fact]
    public async Task SelfServiceToggleClosesNewRequestsButKeepsExistingStatusReachable()
    {
        var nextCalled = false;
        var middleware = new TenantFeatureAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var featureService = new FixedFeatureService(
            new TenantServiceAvailability("limited", true, true, false, false, true)
        );
        var newRequest = new DefaultHttpContext();
        newRequest.Request.Path = "/o/limited/visit-request";
        newRequest.Request.RouteValues["tenant"] = "limited";

        await middleware.InvokeAsync(newRequest, featureService);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, newRequest.Response.StatusCode);

        nextCalled = false;
        var existingStatus = new DefaultHttpContext();
        existingStatus.Request.Path = "/o/limited/visit-request/status/token";
        existingStatus.Request.RouteValues["tenant"] = "limited";

        await middleware.InvokeAsync(existingStatus, featureService);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, existingStatus.Response.StatusCode);

        nextCalled = false;
        var permitReport = new DefaultHttpContext();
        permitReport.Request.Path = "/Reports/Permits";
        permitReport.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "owner")], "Test")
        );
        featureService = new FixedFeatureService(
            new TenantServiceAvailability("limited", false, true, true, false, true)
        );

        await middleware.InvokeAsync(permitReport, featureService);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status302Found, permitReport.Response.StatusCode);
        Assert.Contains(
            "/Home/FeatureUnavailable?feature=Permits",
            permitReport.Response.Headers.Location.ToString()
        );
    }

    private static ServiceProvider CreateProvider(string databasePrefix)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"{databasePrefix}-{Guid.NewGuid():N}")
        );
        return services.BuildServiceProvider();
    }

    private sealed class FixedFeatureService(TenantServiceAvailability availability)
        : ITenantFeatureService
    {
        public bool IsEnabled(string featureKey) => availability.IsEnabled(featureKey);

        public bool IsEnabled(string featureKey, string? tenantIdOrSlug) =>
            availability.IsEnabled(featureKey);

        public TenantServiceAvailability GetCurrent() => availability;

        public TenantServiceAvailability GetForTenant(string? tenantIdOrSlug) => availability;
    }
}
