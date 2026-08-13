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

    private sealed class RecordingPlatformSettingsService : IPlatformSettingsService
    {
        public bool UpdateCalled { get; private set; }

        public PlatformSettingsViewModel Get() => new();

        public void Update(PlatformSettingsViewModel model)
        {
            UpdateCalled = true;
        }
    }
}
