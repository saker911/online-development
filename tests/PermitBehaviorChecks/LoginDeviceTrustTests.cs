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

    [Fact]
    public void ResendRotatesCodeCancelsPendingMessageAndEnforcesCooldown()
    {
        using var fixture = new DeviceChallengeFixture();
        var started = fixture.Start();
        var oldCode = fixture.LatestCode();
        Assert.False(fixture.Service.ResendChallenge(started.ChallengeId).Succeeded);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(fixture.Service.ResendChallenge(started.ChallengeId).Succeeded);
        var newCode = fixture.LatestCode();
        Assert.NotEqual(oldCode, newCode);
        Assert.False(fixture.Service.ResendChallenge(started.ChallengeId).Succeeded);
        using var db = fixture.Factory.CreateDbContext();
        var messages = db.EmailNotificationOutbox.OrderBy(message => message.Id).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal(EmailNotificationOutbox.StatusFailed, messages[0].Status);
        Assert.Equal(EmailNotificationOutbox.StatusPending, messages[1].Status);
        Assert.All(messages, message => Assert.Equal(fixture.User.Email, message.RecipientEmail));
        Assert.False(fixture.Service.Verify(started.ChallengeId, oldCode).Succeeded);
        Assert.True(fixture.Service.Verify(started.ChallengeId, newCode).Succeeded);
        Assert.False(fixture.Service.Verify(started.ChallengeId, newCode).Succeeded);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(fixture.Service.ResendChallenge(started.ChallengeId).Succeeded);
    }

    [Fact]
    public void ResendDoesNotResetFailedAttempts()
    {
        using var fixture = new DeviceChallengeFixture();
        var started = fixture.Start();
        for (var i = 0; i < 4; i++)
            Assert.False(fixture.Service.Verify(started.ChallengeId, "invalid").Succeeded);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(fixture.Service.ResendChallenge(started.ChallengeId).Succeeded);
        Assert.False(fixture.Service.Verify(started.ChallengeId, "invalid").Succeeded);
        Assert.False(fixture.Service.Verify(started.ChallengeId, fixture.LatestCode()).Succeeded);
    }

    [Fact]
    public void ResendIsBoundedAndDoesNotExtendChallengeExpiry()
    {
        using var fixture = new DeviceChallengeFixture();
        var started = fixture.Start();
        for (var i = 0; i < 3; i++)
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.True(fixture.Service.ResendChallenge(started.ChallengeId).Succeeded);
        }
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(fixture.Service.ResendChallenge(started.ChallengeId).Succeeded);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(fixture.Service.GetChallenge(started.ChallengeId));
        Assert.False(fixture.Service.ResendChallenge(started.ChallengeId).Succeeded);
        Assert.False(fixture.Service.Verify(started.ChallengeId, fixture.LatestCode()).Succeeded);
        using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(4, db.EmailNotificationOutbox.Count());
    }

    [Fact]
    public void ResendRejectsDisabledUserAndUnknownChallenge()
    {
        using var fixture = new DeviceChallengeFixture();
        var started = fixture.Start();
        using var db = fixture.Factory.CreateDbContext();
        db.UserAccounts.Single().IsActive = false;
        db.SaveChanges();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(fixture.Service.ResendChallenge(started.ChallengeId).Succeeded);
        Assert.False(fixture.Service.ResendChallenge(new string('a', 64)).Succeeded);
        Assert.Single(db.EmailNotificationOutbox);
    }

    private sealed class DeviceChallengeFixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public MutableSystemClock Clock { get; } = new(DateTime.Now);
        public FixedTenantDbContextFactory Factory { get; }
        public LoginDeviceTrustService Service { get; }
        public UserAccount User { get; } = new()
        {
            TenantId = "resend-tenant",
            Username = "resend-user",
            Email = "resend@example.com",
            IsActive = true,
            IsEmailConfirmed = true,
        };

        public DeviceChallengeFixture()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"resend-{Guid.NewGuid():N}").Options;
            Factory = new(options, new FixedTenantContext(User.TenantId));
            using var db = Factory.CreateDbContext();
            db.UserAccounts.Add(User);
            db.SaveChanges();
            Service = new(Factory, _cache, Clock, NullLogger<LoginDeviceTrustService>.Instance);
        }

        public DeviceChallengeStartResult Start() => Service.BeginChallenge(User, "/Permits", "Chrome/140.0");

        public string LatestCode()
        {
            using var db = Factory.CreateDbContext();
            return Regex.Match(db.EmailNotificationOutbox.OrderByDescending(message => message.Id).First().TextBody, @"\b\d{6}\b").Value;
        }

        public void Dispose() => _cache.Dispose();
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
