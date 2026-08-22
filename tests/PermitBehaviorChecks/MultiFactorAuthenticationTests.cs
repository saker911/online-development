using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OtpNet;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Users;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Authentication)]
public sealed class MultiFactorAuthenticationTests
{
    [Fact]
    public void MfaIsOptionalAndOnlyChallengesAccountsThatEnabledIt()
    {
        var admin = new UserAccount { IsSuperAdmin = true, Role = AppRoles.SystemAdmin };
        var employee = new UserAccount { Role = AppRoles.Employee };
        Assert.False(MultiFactorAuthenticationRequirement.IsRequired(admin));
        Assert.False(MultiFactorAuthenticationRequirement.IsRequired(employee));
        admin.IsActive = true;
        employee.IsActive = true;
        admin.MfaEnabled = true;
        employee.MfaEnabled = true;
        Assert.True(MultiFactorAuthenticationRequirement.IsRequired(admin));
        Assert.True(MultiFactorAuthenticationRequirement.IsRequired(employee));
    }

    [Fact]
    public void EnrollmentProtectsSecretRejectsReplayAndConsumesRecoveryCode()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mfa-{Guid.NewGuid():N}")
            .Options;
        var factory = new FixedTenantDbContextFactory(options, new FixedTenantContext("tenant-mfa"));
        using (var seedDb = factory.CreateDbContext())
        {
            seedDb.Tenants.Add(
                new Tenant { TenantId = "tenant-mfa", Name = "جهة الاختبار", IsActive = true }
            );
            seedDb.UserAccounts.Add(
                new UserAccount
                {
                    TenantId = "tenant-mfa",
                    Username = "1000000001",
                    DisplayName = "مدير الاختبار",
                    FullName = "مدير الاختبار",
                    PhoneNumber = "0500000001",
                    Role = AppRoles.GeneralManager,
                    IsActive = true,
                }
            );
            seedDb.SaveChanges();
        }

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MultiFactorAuthenticationService(
            factory,
            cache,
            new EphemeralDataProtectionProvider(),
            new SystemClock(),
            NullLogger<MultiFactorAuthenticationService>.Instance
        );
        UserAccount user;
        using (var db = factory.CreateDbContext())
        {
            user = db.UserAccounts.AsNoTracking().Single();
        }

        Assert.False(service.IsRequired(user));
        var enrollmentChallengeId = service.BeginEnrollment(user, "/Administration");
        var enrollment = service.GetChallenge(enrollmentChallengeId);
        Assert.NotNull(enrollment);
        Assert.True(enrollment!.RequiresEnrollment);
        Assert.StartsWith("otpauth://totp/", enrollment.ProvisioningUri, StringComparison.Ordinal);
        var currentCode = new Totp(Base32Encoding.ToBytes(enrollment.ManualKey)).ComputeTotp();

        var enrolled = service.Verify(enrollmentChallengeId, currentCode);
        Assert.True(enrolled.Succeeded, enrolled.Message);
        Assert.True(enrolled.WasEnrollment);
        Assert.Equal(8, enrolled.RecoveryCodes?.Count);
        Assert.True(service.IsRequired(enrolled.User));

        using (var assertDb = factory.CreateDbContext())
        {
            var stored = assertDb.UserAccounts.AsNoTracking().Single();
            Assert.True(stored.MfaEnabled);
            Assert.NotEqual(enrollment.ManualKey, stored.MfaSecretProtected);
            Assert.DoesNotContain(enrollment.ManualKey, stored.MfaSecretProtected, StringComparison.Ordinal);
            Assert.Equal(
                8,
                JsonSerializer.Deserialize<string[]>(stored.MfaRecoveryCodeHashesJson)?.Length
            );
        }

        var replayChallengeId = service.BeginChallenge(enrolled.User!, "/Administration");
        var replay = service.Verify(replayChallengeId, currentCode);
        Assert.False(replay.Succeeded);
        Assert.Contains("سابق", replay.Message, StringComparison.Ordinal);

        var recoveryChallengeId = service.BeginChallenge(enrolled.User!, "/Administration");
        var recovery = service.Verify(recoveryChallengeId, enrolled.RecoveryCodes![0]);
        Assert.True(recovery.Succeeded, recovery.Message);

        using var recoveryAssertDb = factory.CreateDbContext();
        var remainingHashes = JsonSerializer.Deserialize<string[]>(
            recoveryAssertDb.UserAccounts.AsNoTracking().Single().MfaRecoveryCodeHashesJson
        );
        Assert.Equal(7, remainingHashes?.Length);
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
