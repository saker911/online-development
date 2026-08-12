using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Users;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static async Task ScenarioPasswordRecoveryTokenIsSecureAndSingleUse(
        TestFixture fixture
    )
    {
        const string originalPassword = "Original2026!";
        const string replacementPassword = "Replacement2026!";
        const string email = "owner@example.test";

        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var user = db.UserAccounts.Single(item => item.Username == "tester");
            user.Email = email;
            user.IsEmailConfirmed = true;
            UserAccountService.SetPassword(user, originalPassword);
            db.SessionRecords.Add(
                new SessionRecord
                {
                    SessionId = "password-reset-session",
                    TenantId = user.TenantId,
                    Username = user.Username,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                    LastActivityUtc = DateTime.UtcNow,
                }
            );
            db.SaveChanges();
        }

        var emailSender = new RecordingPasswordResetEmailSender();
        var service = new AccountPasswordResetService(
            fixture.DbFactory,
            new EphemeralDataProtectionProvider(),
            emailSender,
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Security:PasswordResetTokenMinutes"] = "30",
                }
            ).Build(),
            NullLogger<AccountPasswordResetService>.Instance
        );

        var challenge = service.CreateChallenge(TenantDefaults.DefaultTenantId, "tester");
        Require(challenge != null, "eligible account should receive a reset challenge");
        Require(
            service.Validate(challenge!.Token).Succeeded,
            "new reset token should validate"
        );
        Require(
            !service.Validate(challenge.Token + "tampered").Succeeded,
            "tampered reset token should be rejected"
        );

        var delivery = await service.SendAsync(
            challenge,
            "https://example.test/Account/ResetPassword?token=test"
        );
        Require(delivery.Succeeded, "password reset email should be sent through the sender");
        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var queuedEmail = db.EmailNotificationOutbox.Single(item =>
                item.NotificationType == "PasswordReset"
            );
            Require(
                queuedEmail.RecipientEmail == email,
                "password reset email should target the registered address"
            );
            Require(
                queuedEmail.Status == EmailNotificationOutbox.StatusPending,
                "password reset email should be queued for background delivery"
            );
        }

        Require(
            !service.Reset(challenge.Token, originalPassword).Succeeded,
            "current password should not be accepted as a replacement"
        );
        var reset = service.Reset(challenge.Token, replacementPassword);
        Require(reset.Succeeded, "valid reset token should change the password");

        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var user = db.UserAccounts.Single(item => item.Username == "tester");
            Require(
                !UserAccountService.VerifyPassword(user, originalPassword, out _),
                "old password should stop working"
            );
            Require(
                UserAccountService.VerifyPassword(user, replacementPassword, out _),
                "new password should work"
            );
            Require(
                !db.SessionRecords.Any(item => item.Username == "tester"),
                "password reset should revoke existing sessions"
            );
        }

        Require(
            !service.Reset(challenge.Token, "Another2026!").Succeeded,
            "used reset token should not work a second time"
        );
    }

    private sealed class RecordingPasswordResetEmailSender : ISystemEmailSender
    {
        public bool IsConfigured => true;

        public Task<SystemEmailSendResult> SendAsync(
            string recipientEmail,
            string recipientName,
            string subject,
            string htmlBody,
            string textBody,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(new SystemEmailSendResult(true, "sent"));
        }
    }
}
