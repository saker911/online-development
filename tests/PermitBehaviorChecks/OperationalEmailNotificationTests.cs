using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Tenants;
using Xunit;

namespace PermitBehaviorChecks;

public sealed class OperationalEmailNotificationTests
{
    [Fact]
    public async Task ApprovingPermitThroughServiceCreatesNotificationOutboxEntry()
    {
        await using var fixture = new TestFixture();
        var permit = new Permit
        {
            PermitType = Permit.PermitTypeVisitor,
            DriverName = "حامل تصريح بريدي",
            HolderEmail = "holder@example.com",
            NationalId = "1023456789",
            EmployeePhone = "0501234567",
            VehicleType = "سيدان",
            PlateNumber = "ا ب ج 1234",
            VisitLocation = "المبنى الرئيسي",
            RequiresReturn = false,
        };

        fixture.PermitService.AddPermit(permit, "tester");
        fixture.PermitService.UpdatePermitApprovalStatus(
            permit.PermitNumber,
            "Approved",
            "tester"
        );

        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var notification = await db.EmailNotificationOutbox.SingleAsync(item =>
            item.ReferenceId == permit.PermitNumber
        );
        Assert.Equal("holder@example.com", notification.RecipientEmail);
        Assert.Equal("PermitApproved", notification.NotificationType);
        Assert.Contains("تم اعتماد تصريحك", notification.Subject);
    }

    [Fact]
    public async Task VisitDecisionQueuesOneSafeArabicMessagePerRecipientAndStatus()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableSystemClock(new DateTime(2026, 8, 9, 15, 0, 0));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["App:PublicBaseUrl"] = "https://tasareehapp.test",
                }
            )
            .Build();
        var queue = new OperationalEmailNotificationQueue(
            configuration,
            clock,
            new EphemeralDataProtectionProvider()
        );

        await using var db = await database.Factory.CreateDbContextAsync();
        db.Tenants.Add(
            new Tenant
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Name = "جهة الاختبار",
                Slug = "email-test",
                IsActive = true,
            }
        );
        db.AdministrationSettings.Add(
            new AdministrationSettings
            {
                TenantId = TenantDefaults.DefaultTenantId,
                OrganizationName = "جهة الاختبار",
            }
        );
        var visit = new Visit
        {
            TenantId = TenantDefaults.DefaultTenantId,
            VisitId = "V100",
            VisitorName = "زائر <script>",
            VisitorEmail = "VISITOR@EXAMPLE.COM",
            PhoneNumber = "0500000000",
            NationalId = "1000000000",
            VisitDate = clock.LocalNow.AddHours(1),
            VisitLocation = "البوابة الرئيسية",
            Purpose = "اجتماع",
            VisitedPersonName = "المضيف",
            HostName = "المضيف",
            ApprovalStatus = "Approved",
        };
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        Assert.True(queue.QueueVisitDecision(db, visit));
        await db.SaveChangesAsync();
        Assert.False(queue.QueueVisitDecision(db, visit));
        await db.SaveChangesAsync();

        var notification = await db.EmailNotificationOutbox.SingleAsync();
        Assert.Equal("visitor@example.com", notification.RecipientEmail);
        Assert.Contains("تم اعتماد طلب زيارتك", notification.Subject);
        Assert.Contains("https://tasareehapp.test/o/email-test/visit-request/status", notification.HtmlBody);
        Assert.DoesNotContain("<script>", notification.HtmlBody);
        Assert.Contains("&lt;script&gt;", notification.HtmlBody);
        Assert.Equal(EmailNotificationOutbox.StatusPending, notification.Status);
    }

    [Fact]
    public async Task DispatcherMarksSuccessfullyDeliveredNotificationAsSent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableSystemClock(new DateTime(2026, 8, 9, 16, 0, 0));
        var sender = new FakeEmailSender(new SystemEmailSendResult(true, "sent"));
        await SeedNotificationAsync(database.Factory, clock.UtcNow);
        var dispatcher = new EmailNotificationDispatcher(
            database.Factory,
            sender,
            clock,
            NullLogger<EmailNotificationDispatcher>.Instance
        );

        var processed = await dispatcher.ProcessPendingAsync();

        Assert.Equal(1, processed);
        Assert.Single(sender.Messages);
        await using var db = await database.Factory.CreateDbContextAsync();
        var notification = await db.EmailNotificationOutbox.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(EmailNotificationOutbox.StatusSent, notification.Status);
        Assert.Equal(1, notification.AttemptCount);
        Assert.NotNull(notification.SentAtUtc);
        Assert.Null(notification.LockedAtUtc);
    }

    [Fact]
    public async Task DispatcherSchedulesRetryWithoutLosingFailedNotification()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableSystemClock(new DateTime(2026, 8, 9, 17, 0, 0));
        var sender = new FakeEmailSender(
            new SystemEmailSendResult(false, "provider unavailable")
        );
        await SeedNotificationAsync(database.Factory, clock.UtcNow);
        var dispatcher = new EmailNotificationDispatcher(
            database.Factory,
            sender,
            clock,
            NullLogger<EmailNotificationDispatcher>.Instance
        );

        var processed = await dispatcher.ProcessPendingAsync();

        Assert.Equal(1, processed);
        await using var db = await database.Factory.CreateDbContextAsync();
        var notification = await db.EmailNotificationOutbox.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(EmailNotificationOutbox.StatusPending, notification.Status);
        Assert.Equal(1, notification.AttemptCount);
        Assert.Equal(clock.UtcNow.AddMinutes(1), notification.NextAttemptAtUtc);
        Assert.Contains("provider unavailable", notification.LastError);
    }

    private static async Task SeedNotificationAsync(
        IDbContextFactory<ApplicationDbContext> factory,
        DateTime now
    )
    {
        await using var db = await factory.CreateDbContextAsync();
        db.EmailNotificationOutbox.Add(
            new EmailNotificationOutbox
            {
                TenantId = TenantDefaults.DefaultTenantId,
                NotificationType = "VisitApproved",
                ReferenceType = "Visit",
                ReferenceId = "V200",
                DeduplicationKey = "visitapproved:v200:test@example.com",
                RecipientEmail = "test@example.com",
                RecipientName = "مستلم الاختبار",
                Subject = "نتيجة الطلب",
                HtmlBody = "<p>تم الاعتماد</p>",
                TextBody = "تم الاعتماد",
                Status = EmailNotificationOutbox.StatusPending,
                CreatedAtUtc = now,
                NextAttemptAtUtc = now,
            }
        );
        await db.SaveChangesAsync();
    }

    private sealed class FakeEmailSender(SystemEmailSendResult result) : ISystemEmailSender
    {
        public bool IsConfigured => true;
        public List<string> Messages { get; } = new();

        public Task<SystemEmailSendResult> SendAsync(
            string recipientEmail,
            string recipientName,
            string subject,
            string htmlBody,
            string textBody,
            CancellationToken cancellationToken = default
        )
        {
            Messages.Add(recipientEmail);
            return Task.FromResult(result);
        }
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        private TestDatabase(
            SqliteConnection connection,
            ServiceProvider provider,
            IDbContextFactory<ApplicationDbContext> factory
        )
        {
            _connection = connection;
            _provider = provider;
            Factory = factory;
        }

        public IDbContextFactory<ApplicationDbContext> Factory { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddSingleton<ITenantContext, DefaultTenantContext>();
            services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseSqlite(connection)
            );
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<
                IDbContextFactory<ApplicationDbContext>
            >();
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
            }

            return new TestDatabase(connection, provider, factory);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
