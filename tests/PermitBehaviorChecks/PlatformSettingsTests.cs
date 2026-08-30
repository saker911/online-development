using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VehiclePermitSystemWeb.Controllers;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Platform;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Tenants;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Administration)]
public sealed class PlatformSettingsTests
{
    [Fact]
    public void PlatformSettingsAreGlobalAndNormalizedWhenSaved()
    {
        Assert.False(typeof(ITenantScopedEntity).IsAssignableFrom(typeof(PlatformSettings)));
        Assert.Null(typeof(PlatformSettings).GetProperty("TenantId"));

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"platform-settings-{Guid.NewGuid():N}")
        );
        using var provider = services.BuildServiceProvider();
        var service = new PlatformSettingsService(
            provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
            new ConfigurationBuilder().Build()
        );

        service.Update(
            new PlatformSettingsViewModel
            {
                ProviderName = "  منشأة اختبار  ",
                OfficialEmail = "  INFO@EXAMPLE.COM  ",
                DataHostingLocation = "  موقع حقيقي  ",
            }
        );

        var saved = service.Get();
        Assert.Equal("منشأة اختبار", saved.ProviderName);
        Assert.Equal("info@example.com", saved.OfficialEmail);
        Assert.Equal("موقع حقيقي", saved.DataHostingLocation);
        Assert.True(saved.UpdatedAtUtc.HasValue);
    }

    [Fact]
    public void PlatformSettingsControllerRejectsNonSuperAdminUsers()
    {
        var service = new RecordingPlatformSettingsService();
        var controller = new PlatformSettingsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ClaimTypes.Name, "regular-user")], "Test")
                    ),
                },
            },
        };

        var getResult = Assert.IsType<RedirectToActionResult>(controller.Index());
        var postResult = Assert.IsType<RedirectToActionResult>(
            controller.Index(new PlatformSettingsViewModel { ProviderName = "غير مسموح" })
        );

        Assert.Equal("AccessDenied", getResult.ActionName);
        Assert.Equal("AccessDenied", postResult.ActionName);
        Assert.False(service.UpdateCalled);
    }

    [Fact]
    public void PlatformControllerAllowsOnlyThePlatformOwner()
    {
        var service = new RecordingTenantManagementService();
        var regularController = new PlatformController(service)
        {
            ControllerContext = BuildControllerContext(isSuperAdmin: false),
        };
        var ownerController = new PlatformController(service)
        {
            ControllerContext = BuildControllerContext(isSuperAdmin: true),
        };

        var denied = Assert.IsType<RedirectToActionResult>(regularController.Index());
        Assert.Equal("AccessDenied", denied.ActionName);
        Assert.Equal("Home", denied.ControllerName);

        var allowed = Assert.IsType<ViewResult>(ownerController.Index());
        Assert.IsType<TenantManagementViewModel>(allowed.Model);
        Assert.True(service.DashboardRequested);
    }

    private static ControllerContext BuildControllerContext(bool isSuperAdmin)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "platform-test") };
        if (isSuperAdmin)
        {
            claims.Add(new Claim(AppClaimTypes.SuperAdmin, "true"));
        }

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
            },
        };
    }

    private sealed class RecordingPlatformSettingsService : IPlatformSettingsService
    {
        public bool UpdateCalled { get; private set; }

        public PlatformSettingsViewModel Get() => new();

        public void Update(PlatformSettingsViewModel model)
        {
            UpdateCalled = true;
        }
    }

    private sealed class RecordingTenantManagementService : ITenantManagementService
    {
        public bool DashboardRequested { get; private set; }

        public TenantManagementViewModel GetDashboard()
        {
            DashboardRequested = true;
            return new TenantManagementViewModel();
        }

        public TenantEditorViewModel? GetEditor(string tenantId) => null;
        public TenantOperationResult CreateTenant(TenantEditorViewModel model) => throw new NotSupportedException();
        public TenantSignupResult CreateSignup(TenantSignupViewModel model, bool emailConfirmed = false) => throw new NotSupportedException();
        public TenantSignupResult CreateGoogleTrial(string fullName, string email) => throw new NotSupportedException();
        public TenantCheckoutViewModel? GetCheckout(string tenantId, string checkoutToken) => null;
        public TenantOperationResult UpdateTenant(string tenantId, TenantEditorViewModel model) => throw new NotSupportedException();
        public TenantOperationResult SetTenantActive(string tenantId, bool isActive) => throw new NotSupportedException();
        public TenantOperationResult ReactivateTenant(
            string tenantId,
            string subscriptionStatus,
            DateTime? accessEndsAtUtc
        ) => throw new NotSupportedException();
        public TenantOperationResult DeleteTenant(
            string tenantId,
            string deletionReason,
            bool permanentDeletionConfirmed
        ) => throw new NotSupportedException();
        public TenantOperationResult ActivatePaidSubscription(string tenantId) => throw new NotSupportedException();
    }
}
