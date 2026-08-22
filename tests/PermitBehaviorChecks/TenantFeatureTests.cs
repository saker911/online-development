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

[Trait("Area", TestAreas.Administration)]
public sealed class TenantFeatureTests
{
    [Fact]
    public void PlatformDashboardGroupsUserNamesUnderTheirTenants()
    {
        using var provider = CreateProvider("platform-tenant-directory");
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            db.Tenants.AddRange(
                new Tenant { TenantId = "riyadh", Name = "فرع الرياض", Slug = "riyadh" },
                new Tenant { TenantId = "jeddah", Name = "فرع جدة", Slug = "jeddah" }
            );
            db.UserAccounts.AddRange(
                new UserAccount
                {
                    TenantId = "riyadh",
                    Username = "1023456789",
                    FullName = "مدير فرع الرياض",
                    Role = AppRoles.GeneralManager,
                    IsActive = true,
                },
                new UserAccount
                {
                    TenantId = "riyadh",
                    Username = "1034567890",
                    FullName = "موظف الاستقبال",
                    Role = AppRoles.Receptionist,
                    IsActive = false,
                },
                new UserAccount
                {
                    TenantId = "jeddah",
                    Username = "1045678901",
                    FullName = "مدير فرع جدة",
                    Role = AppRoles.GeneralManager,
                    IsActive = true,
                },
                new UserAccount
                {
                    TenantId = "riyadh",
                    Username = "platform.owner",
                    FullName = "مالك المنصة",
                    Role = AppRoles.SystemAdmin,
                    IsActive = true,
                    IsSuperAdmin = true,
                }
            );
            db.SaveChanges();
        }

        var service = new TenantManagementService(factory, new EphemeralDataProtectionProvider());
        var dashboard = service.GetDashboard();

        var riyadh = Assert.Single(dashboard.Tenants, tenant => tenant.TenantId == "riyadh");
        Assert.Equal(2, riyadh.UserCount);
        Assert.Equal("مدير فرع الرياض", riyadh.Users[0].FullName);
        Assert.True(riyadh.Users[0].IsTenantManager);
        Assert.Contains(
            riyadh.Users,
            account => account.Username == "1034567890" && !account.IsActive
        );

        var jeddah = Assert.Single(dashboard.Tenants, tenant => tenant.TenantId == "jeddah");
        Assert.Single(jeddah.Users);
        Assert.Equal("مدير فرع جدة", jeddah.Users[0].FullName);
        Assert.Equal(3, dashboard.TotalUsers);
        Assert.DoesNotContain(
            dashboard.Tenants.SelectMany(tenant => tenant.Users),
            account => account.Username == "platform.owner"
        );
    }

    [Fact]
    public void TenantCreationCanAtomicallyCreateItsGeneralManager()
    {
        using var provider = CreateProvider("tenant-owner-create");
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var service = new TenantManagementService(factory, new EphemeralDataProtectionProvider());

        var result = service.CreateTenant(new TenantEditorViewModel
        {
            Name = "مدرسة الاختبار",
            OrganizationReference = "123456",
            OwnerUsername = "school.owner",
            OwnerFullName = "مدير المدرسة",
            OwnerEmail = "school-owner@example.com",
            OwnerPhoneNumber = "0501234567",
        });

        Assert.True(result.Succeeded, result.Message);
        using var db = factory.CreateDbContext();
        var tenant = db.Tenants.IgnoreQueryFilters().Single(x => x.TenantId == result.TenantId);
        var owner = db.UserAccounts.IgnoreQueryFilters().Single(x => x.Username == "school.owner");
        var settings = db.AdministrationSettings.IgnoreQueryFilters().Single(x => x.TenantId == result.TenantId);
        Assert.StartsWith("workspace-", tenant.Slug);
        Assert.Equal("123456", tenant.OrganizationReference);
        Assert.Equal(tenant.TenantId, owner.TenantId);
        Assert.Equal(AppRoles.GeneralManager, owner.Role);
        Assert.True(owner.CanManageAdministration);
        Assert.Equal(owner.Username, settings.GeneralManagerUsername);
    }

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
                OwnerUsername = "feature.owner",
                OwnerFullName = "مسؤول الجهة",
                OwnerEmail = "feature-owner@example.com",
                OwnerPhoneNumber = "0501234567",
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

    [Theory]
    [InlineData("/Queue")]
    [InlineData("/Display/WaitingBoard")]
    public async Task QueueToggleClosesOperatorAndDisplayRoutes(string path)
    {
        var nextCalled = false;
        var middleware = new TenantFeatureAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator")], "Test")
        );
        var featureService = new FixedFeatureService(
            new TenantServiceAvailability("limited", true, true, true, false, true)
        );

        await middleware.InvokeAsync(context, featureService);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Contains(
            "/Home/FeatureUnavailable?feature=Queue",
            context.Response.Headers.Location.ToString()
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
