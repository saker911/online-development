using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Tenants;
using Xunit;

namespace PermitBehaviorChecks;

public sealed class SecurityHardeningTests
{
    [Fact]
    public void TenantContextUsesPlatformLinkContextAndIgnoresCustomerHosts()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("customer.example.com");
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var tenantContext = new HttpTenantContext(accessor);

        Assert.Equal(TenantDefaults.DefaultTenantId, tenantContext.TenantId);

        httpContext.Request.QueryString = new QueryString("?tenant=alpha");
        Assert.Equal("alpha", tenantContext.TenantId);

        httpContext.Items[HttpTenantContext.ResolvedTenantItemKey] = "tenant-alpha";
        Assert.Equal("tenant-alpha", tenantContext.TenantId);
    }

    [Theory]
    [InlineData("Google", ExternalAuthenticationDefaults.GoogleScheme)]
    [InlineData("Microsoft", ExternalAuthenticationDefaults.MicrosoftScheme)]
    [InlineData("Hotmail", ExternalAuthenticationDefaults.MicrosoftScheme)]
    [InlineData("Outlook", ExternalAuthenticationDefaults.MicrosoftScheme)]
    [InlineData("unknown-provider", null)]
    public void ExternalProviderNamesAreNormalizedSafely(string provider, string? expected)
    {
        Assert.Equal(expected, ExternalAuthenticationDefaults.NormalizeProvider(provider));
    }

    [Fact]
    public void DisplayAccessKeyIsHashedAndVerifiable()
    {
        const string accessKey = "A-Strong-One-Time-Display-Key-For-Testing";

        var storedValue = DisplayAccessKeyHasher.Hash(accessKey);

        Assert.NotEqual(accessKey, storedValue);
        Assert.True(DisplayAccessKeyHasher.IsHashed(storedValue));
        Assert.True(DisplayAccessKeyHasher.Verify(storedValue, accessKey));
        Assert.False(DisplayAccessKeyHasher.Verify(storedValue, "wrong-key"));
    }

    [Fact]
    public void LoginAttemptGuardLocksAndCanResetAnAccountKey()
    {
        var guard = new LoginAttemptGuard();
        const string username = "1011111111";
        const string tenant = "default";
        const string remoteIp = "127.0.0.1";

        for (var attempt = 0; attempt < 5; attempt++)
        {
            guard.RecordFailure(username, tenant, remoteIp);
        }

        Assert.True(guard.IsBlocked(username, tenant, remoteIp, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);

        guard.Reset(username, tenant, remoteIp);
        Assert.False(guard.IsBlocked(username, tenant, remoteIp, out _));
    }

    [Fact]
    public void CheckoutRequiresAValidTimeLimitedToken()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"checkout-security-{Guid.NewGuid():N}")
        );

        using var serviceProvider = services.BuildServiceProvider();
        var dbFactory = serviceProvider.GetRequiredService<
            IDbContextFactory<ApplicationDbContext>
        >();
        using (var db = dbFactory.CreateDbContext())
        {
            db.Tenants.Add(
                new Tenant
                {
                    TenantId = TenantDefaults.DefaultTenantId,
                    Name = "جهة اختبار آمنة",
                    Slug = TenantDefaults.DefaultTenantId,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    SubscriptionStatus = TenantSubscriptionStatuses.PendingPayment,
                    PlanName = "تشغيل",
                }
            );
            db.SaveChanges();
        }

        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var tenantService = new TenantManagementService(dbFactory, dataProtectionProvider);
        var checkoutProtector = dataProtectionProvider
            .CreateProtector("VehiclePermitSystemWeb.Subscription.Checkout.v1")
            .ToTimeLimitedDataProtector();
        var validToken = checkoutProtector.Protect(
            TenantDefaults.DefaultTenantId,
            TimeSpan.FromMinutes(5)
        );

        Assert.Null(tenantService.GetCheckout(TenantDefaults.DefaultTenantId, "invalid"));
        var checkout = tenantService.GetCheckout(TenantDefaults.DefaultTenantId, validToken);
        Assert.NotNull(checkout);
        Assert.Equal("/o/default", checkout.LoginUrl);
        Assert.Null(tenantService.GetCheckout("another-tenant", validToken));
    }
}
