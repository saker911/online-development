using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Users;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Authentication)]
[Trait("Area", TestAreas.Security)]
public sealed class LoginDeviceTrustTests
{
    [Fact]
    public void UnknownDeviceRequiresEmailCodeThenStoresOnlyHashedTrustedToken()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"trusted-device-{Guid.NewGuid():N}")
            .Options;
        var factory = new FixedTenantDbContextFactory(options, new FixedTenantContext("tenant-device"));
        var user = new UserAccount
        {
            TenantId = "tenant-device",
            Username = "security.user",
            DisplayName = "مستخدم الأمان",
            Email = "security@example.com",
            IsEmailConfirmed = true,
            IsActive = true,
        };
        using (var seed = factory.CreateDbContext())
        {
            seed.Tenants.Add(new Tenant { TenantId = user.TenantId, Name = "جهة", IsActive = true });
            seed.UserAccounts.Add(user);
            seed.SaveChanges();
        }

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new LoginDeviceTrustService(
            factory,
            cache,
            new SystemClock(),
            NullLogger<LoginDeviceTrustService>.Instance
        );

        Assert.False(service.IsTrusted(user, null));
        var started = service.BeginChallenge(
            user,
            "/Permits",
            "Mozilla/5.0 (Windows NT 10.0) Chrome/140.0"
        );
        Assert.True(started.Succeeded, started.Message);
        Assert.Equal("se***@example.com", service.GetChallenge(started.ChallengeId)?.MaskedEmail);
        var repeated = service.BeginChallenge(user, "/Permits", "Mozilla/5.0 Chrome/140.0");
        Assert.Equal(started.ChallengeId, repeated.ChallengeId);

        string code;
        using (var inspect = factory.CreateDbContext())
        {
            var email = inspect.EmailNotificationOutbox.AsNoTracking().Single();
            Assert.Equal("NewDeviceVerification", email.NotificationType);
            code = Regex.Match(email.TextBody, @"\b\d{6}\b").Value;
            Assert.Equal(6, code.Length);
        }

        var rejected = service.Verify(started.ChallengeId, "000000" == code ? "111111" : "000000");
        Assert.False(rejected.Succeeded);

        var verified = service.Verify(started.ChallengeId, code);
        Assert.True(verified.Succeeded, verified.Message);
        Assert.Equal("/Permits", verified.ReturnUrl);
        Assert.NotNull(verified.DeviceToken);
        Assert.True(service.IsTrusted(user, verified.DeviceToken));

        using var assertDb = factory.CreateDbContext();
        var stored = assertDb.TrustedLoginDevices.AsNoTracking().Single();
        Assert.NotEqual(verified.DeviceToken, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Contains("Chrome", stored.DeviceDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceChallengeRequiresActiveAccountWithConfirmedEmail()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"trusted-device-email-{Guid.NewGuid():N}")
            .Options;
        var factory = new FixedTenantDbContextFactory(options, new FixedTenantContext("tenant-device"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new LoginDeviceTrustService(
            factory,
            cache,
            new SystemClock(),
            NullLogger<LoginDeviceTrustService>.Instance
        );

        var result = service.BeginChallenge(
            new UserAccount
            {
                TenantId = "tenant-device",
                Username = "no-email",
                IsActive = true,
                IsEmailConfirmed = false,
            },
            null,
            null
        );
        Assert.False(result.Succeeded);
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    private sealed class FixedTenantDbContextFactory(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext
    ) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options, tenantContext);
    }
}
