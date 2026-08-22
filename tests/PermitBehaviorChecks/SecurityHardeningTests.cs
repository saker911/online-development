using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Uploads;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Security)]
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
    [InlineData("Microsoft", null)]
    [InlineData("Hotmail", null)]
    [InlineData("Outlook", null)]
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
    public void SignupOwnerCanEnterWithoutOperationalPermissionsUntilPaymentActivation()
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
                OrganizationReference = "7001234567",
                OwnerFullName = "مالك الاختبار",
                OwnerUsername = "signup.owner",
                OwnerPhoneNumber = "0501234567",
                OwnerEmail = "owner@example.com",
                Password = "StrongSignup1!",
                ConfirmPassword = "StrongSignup1!",
                AcceptPolicy = true,
            }
        );

        Assert.True(result.Succeeded, result.Message);
        Assert.StartsWith("org-", result.TenantId);
        using (var db = factory.CreateDbContext())
        {
            var tenant = db.Tenants.IgnoreQueryFilters().Single(x => x.TenantId == result.TenantId);
            Assert.True(tenant.IsActive);
            Assert.StartsWith("workspace-", tenant.Slug);
            Assert.NotEqual("secure-signup", tenant.Slug);
            Assert.Equal("7001234567", tenant.OrganizationReference);
            var owner = db.UserAccounts.IgnoreQueryFilters().Single(x => x.Username == "signup.owner");
            var settings = db.AdministrationSettings.IgnoreQueryFilters().Single(x => x.TenantId == result.TenantId);
            Assert.True(owner.IsActive);
            Assert.False(owner.IsEmailConfirmed);
            Assert.Empty(AppPermissions.GetGrantedPermissions(owner));
            Assert.Equal(owner.Username, settings.GeneralManagerUsername);
            Assert.Equal(owner.DisplayName, settings.ManagerName);
            Assert.Equal("مالك الحساب", settings.ManagerTitle);
            Assert.Contains(
                db.Departments.IgnoreQueryFilters(),
                department => department.TenantId == result.TenantId && department.Name == "الإدارة العامة"
            );
        }

        Assert.True(service.ActivatePaidSubscription(result.TenantId).Succeeded);
        using var verifiedDb = factory.CreateDbContext();
        Assert.True(verifiedDb.Tenants.IgnoreQueryFilters().Single(x => x.TenantId == result.TenantId).IsActive);
        var activatedOwner = verifiedDb.UserAccounts.IgnoreQueryFilters().Single(x => x.Username == "signup.owner");
        Assert.True(activatedOwner.IsActive);
        Assert.NotEmpty(AppPermissions.GetGrantedPermissions(activatedOwner));
    }

    [Fact]
    public void GoogleSignupCreatesAnImmediatelyUsableTwoDayTrial()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"google-trial-{Guid.NewGuid():N}")
        );
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var service = new TenantManagementService(factory, new EphemeralDataProtectionProvider());
        var startedAt = DateTime.UtcNow;

        var result = service.CreateGoogleTrial("مستخدم التجربة", "trial@example.com");

        Assert.True(result.Succeeded, result.Message);
        using var db = factory.CreateDbContext();
        var tenant = db.Tenants.IgnoreQueryFilters().Single(x => x.TenantId == result.TenantId);
        var owner = db
            .UserAccounts.IgnoreQueryFilters()
            .Single(x => x.Username == result.OwnerUsername);
        Assert.Equal(TenantSubscriptionStatuses.Trial, tenant.SubscriptionStatus);
        Assert.InRange(
            tenant.TrialEndsAtUtc!.Value,
            startedAt.AddDays(2).AddMinutes(-1),
            startedAt.AddDays(2).AddMinutes(1)
        );
        Assert.True(owner.IsEmailConfirmed);
        Assert.Equal(AppRoles.GeneralManager, owner.Role);
        Assert.NotEmpty(AppPermissions.GetGrantedPermissions(owner));
    }

    [Fact]
    public void EmailConfirmationTokenConfirmsOnlyTheMatchingAccount()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"email-confirmation-{Guid.NewGuid():N}")
        );
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            db.UserAccounts.Add(
                new UserAccount
                {
                    TenantId = "email-tenant",
                    Username = "1023456789",
                    DisplayName = "مالك البريد",
                    Email = "owner@example.com",
                    IsEmailConfirmed = false,
                    IsActive = true,
                }
            );
            db.SaveChanges();
        }

        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var service = new AccountEmailVerificationService(
            factory,
            dataProtectionProvider,
            new ConfigurationBuilder().Build(),
            loggerFactory.CreateLogger<AccountEmailVerificationService>()
        );
        var challenge = service.CreateChallenge("email-tenant", "1023456789");

        Assert.NotNull(challenge);
        var result = service.Confirm(challenge!.Token);
        Assert.True(result.Succeeded, result.Message);
        using var verifiedDb = factory.CreateDbContext();
        Assert.True(
            verifiedDb
                .UserAccounts.IgnoreQueryFilters()
                .Single(x => x.Username == "1023456789")
                .IsEmailConfirmed
        );
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
                OwnerUsername = "seeded.owner",
                OwnerFullName = "مسؤول الجهة",
                OwnerEmail = "seeded-owner@example.com",
                OwnerPhoneNumber = "0501234567",
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
                OwnerUsername = "delete.owner",
                OwnerFullName = "مسؤول الجهة",
                OwnerEmail = "delete-owner@example.com",
                OwnerPhoneNumber = "0501234567",
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
    public async Task UploadedImagesRejectDeclaredTypeThatDoesNotMatchContent()
    {
        await using var source = new MemoryStream();
        using (var image = new Image<Rgba32>(2, 2, Color.White))
        {
            await image.SaveAsPngAsync(source);
        }

        var pngBytes = source.ToArray();
        await using var stream = new MemoryStream(pngBytes);
        var file = new FormFile(stream, 0, pngBytes.Length, "logo", "logo.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg",
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdministrationImageStorage.ProcessAsync(file)
        );

        Assert.Contains("لا يطابق", exception.Message);
    }

    [Theory]
    [InlineData("stream: OK\0", false)]
    [InlineData("stream: Eicar-Signature FOUND\0", true)]
    public async Task ClamAvScannerAcceptsCleanFilesAndRejectsThreats(
        string response,
        bool shouldReject
    )
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = ServeClamAvResponseAsync(listener, response);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["UploadSecurity:Antivirus:Enabled"] = "true",
                    ["UploadSecurity:Antivirus:Required"] = "true",
                    ["UploadSecurity:Antivirus:Host"] = "127.0.0.1",
                    ["UploadSecurity:Antivirus:Port"] = port.ToString(),
                    ["UploadSecurity:Antivirus:TimeoutSeconds"] = "5",
                }
            )
            .Build();
        var scanner = new ClamAvUploadThreatScanner(
            configuration,
            NullLogger<ClamAvUploadThreatScanner>.Instance
        );
        await using var content = new MemoryStream(Encoding.ASCII.GetBytes("test upload"));

        if (shouldReject)
        {
            await Assert.ThrowsAsync<UnsafeUploadException>(() =>
                scanner.ScanAsync(content, "test-upload.bin")
            );
        }
        else
        {
            await scanner.ScanAsync(content, "test-upload.bin");
        }

        await serverTask;
    }

    [Fact]
    public async Task ClamAvScannerFailsClosedWhenRequiredScannerIsUnavailable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var unavailablePort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["UploadSecurity:Antivirus:Enabled"] = "true",
                    ["UploadSecurity:Antivirus:Required"] = "true",
                    ["UploadSecurity:Antivirus:Host"] = "127.0.0.1",
                    ["UploadSecurity:Antivirus:Port"] = unavailablePort.ToString(),
                    ["UploadSecurity:Antivirus:TimeoutSeconds"] = "2",
                }
            )
            .Build();
        var scanner = new ClamAvUploadThreatScanner(
            configuration,
            NullLogger<ClamAvUploadThreatScanner>.Instance
        );
        await using var content = new MemoryStream(Encoding.ASCII.GetBytes("test upload"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scanner.ScanAsync(content, "test-upload.bin")
        );

        Assert.Contains("تعذر إجراء الفحص الأمني", exception.Message);
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

    [Fact]
    public void TenantUserCreationAndUpdateCannotCrossTenantBoundary()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tenant-user-writes-{Guid.NewGuid():N}")
            .Options;
        var tenantContext = new FixedTenantContext("tenant-a");
        var factory = new FixedTenantDbContextFactory(options, tenantContext);

        using (var seed = factory.CreateDbContext())
        {
            seed.Tenants.AddRange(
                new Tenant
                {
                    TenantId = "tenant-a",
                    Name = "جهة ألف",
                    Slug = "tenant-a",
                    IsActive = true,
                },
                new Tenant
                {
                    TenantId = "tenant-b",
                    Name = "جهة باء",
                    Slug = "tenant-b",
                    IsActive = true,
                }
            );
            seed.SaveChanges();
        }

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clock = new SystemClock();
        var service = new UserAdminService(
            factory,
            new ConfigurationBuilder().Build(),
            cache,
            clock,
            new PermitAuditService(),
            new UserSessionService(factory, clock)
        );
        var user = new UserAccount
        {
            TenantId = "tenant-b",
            Username = "1000000011",
            DisplayName = "موظف جهة ألف",
            FullName = "موظف جهة ألف",
            PhoneNumber = "0500000011",
            Role = AppRoles.Employee,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(user);

        Assert.True(service.CreateUser(user, "TenantBoundary2026!"));

        using (var assertCreate = factory.CreateDbContext())
        {
            var stored = assertCreate.UserAccounts.Single(item => item.Username == user.Username);
            Assert.Equal("tenant-a", stored.TenantId);
        }

        user.TenantId = "tenant-b";
        user.JobTitle = "موظف محدث";
        Assert.True(service.UpdateUser(user));

        using var assertUpdate = factory.CreateDbContext();
        var updated = assertUpdate.UserAccounts.Single(item => item.Username == user.Username);
        Assert.Equal("tenant-a", updated.TenantId);
        Assert.Equal("موظف محدث", updated.JobTitle);
        Assert.Empty(
            assertUpdate.UserAccounts.IgnoreQueryFilters().Where(item => item.TenantId == "tenant-b")
        );
    }

    [Fact]
    public void PlatformOwnerCanExplicitlyCreateUserInSelectedTenant()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"platform-user-writes-{Guid.NewGuid():N}")
            .Options;
        var factory = new FixedTenantDbContextFactory(
            options,
            new FixedTenantContext(TenantDefaults.DefaultTenantId)
        );
        using (var seed = factory.CreateDbContext())
        {
            seed.Tenants.AddRange(
                new Tenant
                {
                    TenantId = TenantDefaults.DefaultTenantId,
                    Name = TenantDefaults.DefaultTenantName,
                    Slug = TenantDefaults.DefaultTenantId,
                    IsActive = true,
                },
                new Tenant
                {
                    TenantId = "tenant-b",
                    Name = "جهة باء",
                    Slug = "tenant-b",
                    IsActive = true,
                }
            );
            seed.SaveChanges();
        }

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clock = new SystemClock();
        var service = new UserAdminService(
            factory,
            new ConfigurationBuilder().Build(),
            cache,
            clock,
            new PermitAuditService(),
            new UserSessionService(factory, clock)
        );
        var user = new UserAccount
        {
            TenantId = "tenant-b",
            Username = "1000000012",
            DisplayName = "مدير جهة باء",
            FullName = "مدير جهة باء",
            PhoneNumber = "0500000012",
            Role = AppRoles.GeneralManager,
            IsActive = true,
        };
        AppPermissions.ApplyRoleDefaults(user);

        Assert.True(service.CreateUser(user, "PlatformSelection2026!", allowTenantSelection: true));

        using var assertDb = factory.CreateDbContext();
        var stored = assertDb
            .UserAccounts.IgnoreQueryFilters()
            .Single(item => item.Username == user.Username);
        Assert.Equal("tenant-b", stored.TenantId);
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

    private static async Task ServeClamAvResponseAsync(
        TcpListener listener,
        string response
    )
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var command = new byte["zINSTREAM\0".Length];
        await stream.ReadExactlyAsync(command);
        Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(command));

        var lengthBuffer = new byte[sizeof(uint)];
        while (true)
        {
            await stream.ReadExactlyAsync(lengthBuffer);
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(lengthBuffer);
            if (chunkLength == 0)
            {
                break;
            }

            var chunk = new byte[checked((int)chunkLength)];
            await stream.ReadExactlyAsync(chunk);
        }

        await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        await stream.FlushAsync();
        listener.Stop();
    }
}
