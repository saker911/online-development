using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VehiclePermitSystemWeb.Services.Tenants;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Authentication)]
public sealed class AuthenticationIdentifierTests
{
    [Fact]
    public void AccountCanSignInWithUsernameEmailOrMobile()
    {
        using var provider = CreateProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        const string password = "LoginAlias2026!";

        using (var db = factory.CreateDbContext())
        {
            SeedActiveTenant(db);
            var user = new UserAccount
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Username = "employee.login",
                DisplayName = "مستخدم الدخول",
                FullName = "مستخدم الدخول",
                Email = "employee.login@example.com",
                PhoneNumber = "0501234567",
                IsActive = true,
                Role = AppRoles.Employee,
            };
            UserAccountService.SetPassword(user, password);
            db.UserAccounts.Add(user);
            db.SaveChanges();
        }

        using var verificationDb = factory.CreateDbContext();
        Assert.True(UserAccountService.ValidateCredentials(verificationDb, "employee.login", password, out _, out _));
        Assert.True(UserAccountService.ValidateCredentials(verificationDb, "employee.login@example.com", password, out _, out _));
        Assert.True(UserAccountService.ValidateCredentials(verificationDb, "0501234567", password, out _, out _));
    }

    [Fact]
    public void DuplicateContactIdentifierDoesNotSelectAnArbitraryAccount()
    {
        using var provider = CreateProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        const string password = "DuplicateContact2026!";

        using (var db = factory.CreateDbContext())
        {
            SeedActiveTenant(db);
            foreach (var username in new[] { "employee.first", "employee.second" })
            {
                var user = new UserAccount
                {
                    TenantId = TenantDefaults.DefaultTenantId,
                    Username = username,
                    DisplayName = username,
                    FullName = username,
                    PhoneNumber = "0507654321",
                    IsActive = true,
                    Role = AppRoles.Employee,
                };
                UserAccountService.SetPassword(user, password);
                db.UserAccounts.Add(user);
            }

            db.SaveChanges();
        }

        using var verificationDb = factory.CreateDbContext();
        Assert.False(UserAccountService.ValidateCredentials(verificationDb, "0507654321", password, out _, out _));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"authentication-identifiers-{Guid.NewGuid():N}")
        );
        return services.BuildServiceProvider();
    }

    private static void SeedActiveTenant(ApplicationDbContext db)
    {
        db.Tenants.Add(
            new Tenant
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Name = "جهة اختبار الدخول",
                Slug = TenantDefaults.DefaultTenantId,
                IsActive = true,
                SubscriptionStatus = TenantSubscriptionStatuses.Active,
            }
        );
    }
}
