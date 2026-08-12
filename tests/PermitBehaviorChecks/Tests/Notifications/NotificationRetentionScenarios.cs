using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Models.Entities;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioNotificationMutationsCannotCrossTenantBoundary(
        TestFixture fixture
    )
    {
        long foreignNotificationId;
        using (var db = fixture.DbFactory.CreateDbContext())
        {
            db.Tenants.Add(
                new Tenant
                {
                    TenantId = "other-tenant",
                    Name = "جهة أخرى",
                    Slug = "other-tenant",
                    SubscriptionStatus = TenantSubscriptionStatuses.Active,
                }
            );
            var foreignNotification = new InAppNotification
            {
                TenantId = "other-tenant",
                RecipientUsername = "tester",
                Category = NotificationCategories.Security,
                Severity = NotificationSeverities.Critical,
                Title = "تنبيه جهة أخرى",
                SourceKey = "foreign-notification",
                OccurredAtUtc = fixture.Clock.UtcNow,
                CreatedAtUtc = fixture.Clock.UtcNow,
            };
            db.InAppNotifications.Add(foreignNotification);
            db.SaveChanges();
            foreignNotificationId = foreignNotification.Id;
        }

        var changed = fixture.NotificationCenterService.MarkRead(
            foreignNotificationId,
            "tester"
        );
        Require(!changed, "current tenant must not mutate another tenant notification");

        using var verificationDb = fixture.DbFactory.CreateDbContext();
        var foreign = verificationDb
            .InAppNotifications.IgnoreQueryFilters()
            .Single(item => item.Id == foreignNotificationId);
        Require(foreign.ReadAtUtc == null, "foreign notification must remain unread");
        Require(
            !verificationDb.InAppNotifications.Any(item => item.Id == foreignNotificationId),
            "foreign notification must be hidden by tenant query filter"
        );
        return Task.CompletedTask;
    }

    private static async Task ScenarioTenantRetentionRemovesOnlyEligibleOldRecords(
        TestFixture fixture
    )
    {
        var now = fixture.Clock.UtcNow;
        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var tenant = db.Tenants.Single(item => item.TenantId == TenantDefaults.DefaultTenantId);
            tenant.NotificationRetentionDays = 30;
            tenant.EmailOutboxRetentionDays = 14;
            tenant.AuditLogRetentionDays = 90;

            db.InAppNotifications.AddRange(
                new InAppNotification
                {
                    RecipientUsername = "tester",
                    Category = NotificationCategories.System,
                    Severity = NotificationSeverities.Info,
                    Title = "قديم",
                    SourceKey = "retention-old",
                    OccurredAtUtc = now.AddDays(-45),
                    CreatedAtUtc = now.AddDays(-45),
                },
                new InAppNotification
                {
                    RecipientUsername = "tester",
                    Category = NotificationCategories.System,
                    Severity = NotificationSeverities.Info,
                    Title = "حديث",
                    SourceKey = "retention-current",
                    OccurredAtUtc = now.AddDays(-5),
                    CreatedAtUtc = now.AddDays(-5),
                }
            );
            db.EmailNotificationOutbox.AddRange(
                BuildOutbox("old-sent", EmailNotificationOutbox.StatusSent, now.AddDays(-20)),
                BuildOutbox("old-pending", EmailNotificationOutbox.StatusPending, now.AddDays(-20))
            );
            db.AuditLogs.AddRange(
                BuildAudit("old-audit", now.AddDays(-120)),
                BuildAudit("current-audit", now.AddDays(-10))
            );
            db.SaveChanges();
        }

        var result = await fixture.DataRetentionService.RunAsync();
        Require(result.NotificationsDeleted == 1, "retention must delete one old notification");
        Require(result.EmailRecordsDeleted == 1, "retention must delete only completed old email");
        Require(result.AuditLogsDeleted == 1, "retention must delete one audit outside policy");

        using var verificationDb = fixture.DbFactory.CreateDbContext();
        Require(
            await verificationDb.InAppNotifications.AnyAsync(item => item.SourceKey == "retention-current"),
            "current notification must remain"
        );
        Require(
            !await verificationDb.InAppNotifications.AnyAsync(item => item.SourceKey == "retention-old"),
            "old notification must be removed"
        );
        Require(
            await verificationDb.EmailNotificationOutbox.AnyAsync(item => item.DeduplicationKey == "old-pending"),
            "pending email must never be deleted by retention"
        );
        Require(
            !await verificationDb.EmailNotificationOutbox.AnyAsync(item => item.DeduplicationKey == "old-sent"),
            "completed old email must be removed"
        );
        Require(
            await verificationDb.AuditLogs.AnyAsync(item => item.EntityId == "current-audit"),
            "audit inside policy must remain"
        );
        Require(
            verificationDb.Tenants.Single().LastRetentionRunAtUtc == now,
            "tenant cleanup timestamp must be recorded"
        );
    }

    private static EmailNotificationOutbox BuildOutbox(
        string key,
        string status,
        DateTime createdAt
    ) =>
        new()
        {
            NotificationType = "Test",
            ReferenceType = "Test",
            ReferenceId = key,
            DeduplicationKey = key,
            RecipientEmail = "test@example.test",
            RecipientName = "Test",
            Subject = "Test",
            HtmlBody = "Test",
            TextBody = "Test",
            Status = status,
            CreatedAtUtc = createdAt,
            NextAttemptAtUtc = createdAt,
        };

    private static AuditLog BuildAudit(string id, DateTime occurredAt) =>
        new()
        {
            Username = "tester",
            ActionType = "Test",
            ActionLabel = "اختبار",
            EntityType = "Test",
            EntityId = id,
            Message = "اختبار الاحتفاظ",
            Source = "Tests",
            RecordedBy = "tester",
            ActualActorUsername = "tester",
            OccurredAt = occurredAt,
            Success = true,
        };
}
