using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
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

    [Fact]
    public void ExternalLoginUsesImmutableProviderIdentityInsteadOfEmail()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"external-login-{Guid.NewGuid():N}")
        );
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var service = new ExternalLoginService(factory);
        var firstPrincipal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim("iss", "https://accounts.google.com"),
                    new Claim("sub", "immutable-google-subject"),
                    new Claim(ClaimTypes.Email, "first@example.com"),
                ],
                "Google"
            )
        );
        var identity = service.ReadIdentity(firstPrincipal, "Google");

        Assert.NotNull(identity);
        Assert.True(service.Link(identity!, "default", "1023456789").Succeeded);

        var changedEmailPrincipal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim("iss", "https://accounts.google.com"),
                    new Claim("sub", "immutable-google-subject"),
                    new Claim(ClaimTypes.Email, "changed@example.com"),
                ],
                "Google"
            )
        );
        var changedIdentity = service.ReadIdentity(changedEmailPrincipal, "Google");
        var login = service.FindLogin(changedIdentity!);

        Assert.NotNull(login);
        Assert.Equal("1023456789", login.Username);
        Assert.Null(
            service.FindLogin(
                changedIdentity! with { Subject = "different-subject" }
            )
        );
    }

    [Fact]
    public void LoginAndSignupGuardsPersistAcrossServiceInstances()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"persistent-guards-{Guid.NewGuid():N}")
        );
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        var firstLoginGuard = new LoginAttemptGuard(factory);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            firstLoginGuard.RecordFailure("1023456789", "default", "127.0.0.1");
        }
        var secondLoginGuard = new LoginAttemptGuard(factory);
        Assert.True(
            secondLoginGuard.IsBlocked("1023456789", "default", "127.0.0.1", out _)
        );

        var firstSignupGuard = new SignupAttemptGuard(factory);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            firstSignupGuard.RecordSuccess("127.0.0.1");
        }
        var secondSignupGuard = new SignupAttemptGuard(factory);
        Assert.False(secondSignupGuard.CanCreate("127.0.0.1", out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void SignupOwnerStaysInactiveUntilPaymentActivation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"pending-signup-{Guid.NewGuid():N}")
        );
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var service = new TenantManagementService(factory, new EphemeralDataProtectionProvider());
        var result = service.CreateSignup(
            new TenantSignupViewModel
            {
                PlanCode = "monthly",
                CompanyName = "جهة اختبار التسجيل",
                TenantId = "secure-signup",
                OwnerFullName = "مالك الاختبار",
                OwnerUsername = "1023456789",
                OwnerPhoneNumber = "0501234567",
                OwnerEmail = "owner@example.com",
                Password = "StrongSignup1!",
                ConfirmPassword = "StrongSignup1!",
                AcceptPolicy = true,
            }
        );

        Assert.True(result.Succeeded, result.Message);
        using (var db = factory.CreateDbContext())
        {
            Assert.False(db.Tenants.IgnoreQueryFilters().Single(x => x.TenantId == "secure-signup").IsActive);
            var owner = db.UserAccounts.IgnoreQueryFilters().Single(x => x.Username == "1023456789");
            var settings = db.AdministrationSettings.IgnoreQueryFilters().Single(x => x.TenantId == "secure-signup");
            Assert.False(owner.IsActive);
            Assert.Equal(owner.Username, settings.GeneralManagerUsername);
            Assert.Equal(owner.DisplayName, settings.ManagerName);
            Assert.Equal("مالك الحساب", settings.ManagerTitle);
            Assert.Contains(
                db.Departments.IgnoreQueryFilters(),
                department => department.TenantId == "secure-signup" && department.Name == "الإدارة العامة"
            );
        }

        Assert.True(service.ActivatePaidSubscription("secure-signup").Succeeded);
        using var verifiedDb = factory.CreateDbContext();
        Assert.True(verifiedDb.Tenants.IgnoreQueryFilters().Single(x => x.TenantId == "secure-signup").IsActive);
        Assert.True(verifiedDb.UserAccounts.IgnoreQueryFilters().Single(x => x.Username == "1023456789").IsActive);
    }

    [Fact]
    public void TenantCreationSeedsAnIsolatedDefaultDepartment()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"tenant-seed-{Guid.NewGuid():N}")
        );
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var service = new TenantManagementService(factory, new EphemeralDataProtectionProvider());

        var result = service.CreateTenant(
            new TenantEditorViewModel
            {
                TenantId = "seeded-tenant",
                Name = "جهة مهيأة",
                Slug = "seeded-tenant",
                DepartmentName = string.Empty,
            }
        );

        Assert.True(result.Succeeded, result.Message);
        using var db = factory.CreateDbContext();
        var settings = db.AdministrationSettings.IgnoreQueryFilters().Single(x => x.TenantId == "seeded-tenant");
        var department = db.Departments.IgnoreQueryFilters().Single(x => x.TenantId == "seeded-tenant");
        Assert.Equal("الإدارة العامة", settings.DepartmentName);
        Assert.Equal(settings.DepartmentName, department.Name);
        Assert.True(department.IsActive);
    }

    [Fact]
    public void TenantDeletionRequiresStopAndExactNameThenRemovesScopedData()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"tenant-delete-{Guid.NewGuid():N}")
        );
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var service = new TenantManagementService(factory, new EphemeralDataProtectionProvider());

        var created = service.CreateTenant(
            new TenantEditorViewModel
            {
                TenantId = "deletable-tenant",
                Name = "جهة قابلة للحذف",
                Slug = "deletable-tenant",
            }
        );
        Assert.True(created.Succeeded, created.Message);

        using (var db = factory.CreateDbContext())
        {
            db.UserAccounts.Add(
                new UserAccount
                {
                    TenantId = "deletable-tenant",
                    Username = "1999999999",
                    DisplayName = "مستخدم اختبار",
                    FullName = "مستخدم اختبار",
                    PhoneNumber = "0500000000",
                }
            );
            db.SessionRecords.Add(
                new SessionRecord
                {
                    TenantId = "deletable-tenant",
                    SessionId = "tenant-delete-session",
                    Username = "1999999999",
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                    LastActivityUtc = DateTime.UtcNow,
                }
            );
            db.SaveChanges();
        }

        Assert.False(service.DeleteTenant("deletable-tenant", "جهة قابلة للحذف").Succeeded);
        Assert.True(service.SetTenantActive("deletable-tenant", false).Succeeded);
        using (var stoppedDb = factory.CreateDbContext())
        {
            Assert.DoesNotContain(
                stoppedDb.SessionRecords.IgnoreQueryFilters(),
                item => item.TenantId == "deletable-tenant"
            );
        }
        Assert.False(service.DeleteTenant("deletable-tenant", "اسم غير مطابق").Succeeded);
        Assert.True(service.DeleteTenant("deletable-tenant", "جهة قابلة للحذف").Succeeded);

        using var verifiedDb = factory.CreateDbContext();
        Assert.DoesNotContain(
            verifiedDb.Tenants.IgnoreQueryFilters(),
            item => item.TenantId == "deletable-tenant"
        );
        Assert.DoesNotContain(
            verifiedDb.UserAccounts.IgnoreQueryFilters(),
            item => item.TenantId == "deletable-tenant"
        );
        Assert.DoesNotContain(
            verifiedDb.SessionRecords.IgnoreQueryFilters(),
            item => item.TenantId == "deletable-tenant"
        );
    }

    [Fact]
    public void DepartmentNamesAreUniqueWithinEachTenant()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"department-index-{Guid.NewGuid():N}")
            .Options;
        using var db = new ApplicationDbContext(options, new DefaultTenantContext());
        var index = db.Model.FindEntityType(typeof(Department))!
            .GetIndexes()
            .Single(item => item.IsUnique && item.Properties.Any(property => property.Name == nameof(Department.Name)));

        Assert.Equal(
            new[] { nameof(Department.TenantId), nameof(Department.Name) },
            index.Properties.Select(property => property.Name)
        );
    }

    [Fact]
    public async Task UploadedImagesAreDecodedAndReencoded()
    {
        await using var source = new MemoryStream();
        using (var image = new Image<Rgba32>(2, 2, Color.White))
        {
            await image.SaveAsPngAsync(source);
        }
        var pngBytes = source.ToArray();
        await using var stream = new MemoryStream(pngBytes);
        var file = new FormFile(stream, 0, pngBytes.Length, "logo", "logo.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png",
        };

        var processed = await AdministrationImageStorage.ProcessAsync(file);

        Assert.Equal("image/png", processed.ContentType);
        Assert.NotEmpty(processed.Data);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, processed.Data[..4]);
    }

    [Fact]
    public void TenantQueryFiltersHideOtherTenantOperationalRecords()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tenant-isolation-{Guid.NewGuid():N}")
            .Options;
        using (var seed = new ApplicationDbContext(options, new FixedTenantContext("tenant-a")))
        {
            seed.Permits.AddRange(
                new Permit { TenantId = "tenant-a", PermitNumber = "A-PERMIT" },
                new Permit { TenantId = "tenant-b", PermitNumber = "B-PERMIT" }
            );
            seed.Visits.AddRange(
                new Visit { TenantId = "tenant-a", VisitId = "A-VISIT" },
                new Visit { TenantId = "tenant-b", VisitId = "B-VISIT" }
            );
            seed.UserAccounts.AddRange(
                new UserAccount { TenantId = "tenant-a", Username = "1000000001" },
                new UserAccount { TenantId = "tenant-b", Username = "1000000002" }
            );
            seed.SaveChanges();
        }

        using var tenantA = new ApplicationDbContext(options, new FixedTenantContext("tenant-a"));
        Assert.Equal("A-PERMIT", tenantA.Permits.Single().PermitNumber);
        Assert.Equal("A-VISIT", tenantA.Visits.Single().VisitId);
        Assert.Equal("1000000001", tenantA.UserAccounts.Single().Username);
        Assert.Null(tenantA.Permits.SingleOrDefault(item => item.PermitNumber == "B-PERMIT"));
        Assert.Null(tenantA.Visits.SingleOrDefault(item => item.VisitId == "B-VISIT"));
        Assert.Null(tenantA.UserAccounts.SingleOrDefault(item => item.Username == "1000000002"));
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }
}
