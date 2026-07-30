using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;

namespace VehiclePermitSystemWeb.Services.Users
{
    public interface IAccountEmailVerificationService
    {
        bool IsDeliveryConfigured { get; }
        EmailVerificationChallenge? CreateChallenge(string tenantId, string username);
        EmailConfirmationResult Confirm(string token);
        Task<EmailDeliveryResult> SendAsync(
            EmailVerificationChallenge challenge,
            string confirmationUrl,
            CancellationToken cancellationToken = default
        );
    }

    public sealed record EmailVerificationChallenge(
        string TenantId,
        string Username,
        string Email,
        string DisplayName,
        string Token
    );

    public sealed record EmailConfirmationResult(
        bool Succeeded,
        string Message,
        string TenantId = "",
        string Username = ""
    );

    public sealed record EmailDeliveryResult(bool Succeeded, string Message);

    public sealed class AccountEmailVerificationService : IAccountEmailVerificationService
    {
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ITimeLimitedDataProtector _protector;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountEmailVerificationService> _logger;

        public AccountEmailVerificationService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration,
            ILogger<AccountEmailVerificationService> logger
        )
        {
            _dbContextFactory = dbContextFactory;
            _protector = dataProtectionProvider
                .CreateProtector("VehiclePermitSystemWeb.Account.EmailConfirmation.v1")
                .ToTimeLimitedDataProtector();
            _configuration = configuration;
            _logger = logger;
        }

        public bool IsDeliveryConfigured =>
            !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Host"])
            && !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Username"])
            && !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Password"])
            && !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:FromAddress"]);

        public EmailVerificationChallenge? CreateChallenge(string tenantId, string username)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedTenantId = (tenantId ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedUsername = (username ?? string.Empty).Trim();
            var user = db
                .UserAccounts.IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefault(item =>
                    item.TenantId == normalizedTenantId
                    && item.Username == normalizedUsername
                    && item.IsActive
                );
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                return null;
            }

            var payload = JsonSerializer.Serialize(
                new EmailVerificationTokenPayload(
                    user.TenantId,
                    user.Username,
                    user.Email.Trim().ToLowerInvariant()
                )
            );
            return new EmailVerificationChallenge(
                user.TenantId,
                user.Username,
                user.Email.Trim(),
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                _protector.Protect(payload, TokenLifetime)
            );
        }

        public EmailConfirmationResult Confirm(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new EmailConfirmationResult(false, "رابط التأكيد غير صالح.");
            }

            EmailVerificationTokenPayload? payload;
            try
            {
                var json = _protector.Unprotect(token, out var expiresAt);
                if (expiresAt <= DateTimeOffset.UtcNow)
                {
                    return new EmailConfirmationResult(
                        false,
                        "انتهت صلاحية رابط التأكيد. اطلب رسالة جديدة."
                    );
                }

                payload = JsonSerializer.Deserialize<EmailVerificationTokenPayload>(json);
            }
            catch (Exception exception) when (
                exception is CryptographicException or JsonException
            )
            {
                return new EmailConfirmationResult(false, "رابط التأكيد غير صالح أو منتهي.");
            }

            if (
                payload == null
                || string.IsNullOrWhiteSpace(payload.TenantId)
                || string.IsNullOrWhiteSpace(payload.Username)
                || string.IsNullOrWhiteSpace(payload.Email)
            )
            {
                return new EmailConfirmationResult(false, "رابط التأكيد غير صالح.");
            }

            using var db = _dbContextFactory.CreateDbContext();
            var user = db.UserAccounts.IgnoreQueryFilters().FirstOrDefault(item =>
                item.TenantId == payload.TenantId && item.Username == payload.Username
            );
            if (
                user == null
                || !string.Equals(
                    user.Email.Trim(),
                    payload.Email,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return new EmailConfirmationResult(false, "تعذر مطابقة حساب التأكيد.");
            }

            if (!user.IsEmailConfirmed)
            {
                user.IsEmailConfirmed = true;
                db.SaveChanges();
            }

            return new EmailConfirmationResult(
                true,
                "تم تأكيد بريدك الإلكتروني. يمكنك تسجيل الدخول الآن.",
                user.TenantId,
                user.Username
            );
        }

        public async Task<EmailDeliveryResult> SendAsync(
            EmailVerificationChallenge challenge,
            string confirmationUrl,
            CancellationToken cancellationToken = default
        )
        {
            if (!IsDeliveryConfigured)
            {
                return new EmailDeliveryResult(
                    false,
                    "بريد الموقع غير مهيأ للإرسال بعد."
                );
            }

            var host = _configuration["Email:Smtp:Host"]!.Trim();
            var port = _configuration.GetValue("Email:Smtp:Port", 587);
            var username = _configuration["Email:Smtp:Username"]!.Trim();
            var password = _configuration["Email:Smtp:Password"]!;
            var fromAddress = _configuration["Email:Smtp:FromAddress"]!.Trim();
            var fromName = _configuration["Email:Smtp:FromName"]?.Trim();
            var enableSsl = _configuration.GetValue("Email:Smtp:EnableSsl", true);
            var safeName = HtmlEncoder.Default.Encode(challenge.DisplayName);
            var safeUrl = HtmlEncoder.Default.Encode(confirmationUrl);

            using var message = new MailMessage
            {
                From = new MailAddress(
                    fromAddress,
                    string.IsNullOrWhiteSpace(fromName) ? "منصة التصاريح" : fromName
                ),
                Subject = "تأكيد بريدك في منصة التصاريح",
                IsBodyHtml = true,
                Body =
                    $"""
                    <div dir="rtl" style="font-family:Arial,sans-serif;line-height:1.8;color:#111827">
                      <h2>أهلاً {safeName}</h2>
                      <p>اضغط الزر التالي لتأكيد بريدك وإكمال إنشاء حساب الجهة.</p>
                      <p><a href="{safeUrl}" style="display:inline-block;padding:12px 20px;background:#111827;color:#fff;text-decoration:none;border-radius:6px">تأكيد البريد الإلكتروني</a></p>
                      <p style="color:#6b7280">صلاحية الرابط 24 ساعة. إذا لم تطلب إنشاء الحساب فتجاهل الرسالة.</p>
                    </div>
                    """,
            };
            message.To.Add(new MailAddress(challenge.Email, challenge.DisplayName));

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = new NetworkCredential(username, password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Timeout = 15000,
            };

            try
            {
                await client.SendMailAsync(message, cancellationToken);
                return new EmailDeliveryResult(true, "تم إرسال رسالة التأكيد.");
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to send account verification email for tenant {TenantId}.",
                    challenge.TenantId
                );
                return new EmailDeliveryResult(
                    false,
                    "تعذر إرسال رسالة التأكيد الآن. حاول مرة أخرى لاحقاً."
                );
            }
        }

        private sealed record EmailVerificationTokenPayload(
            string TenantId,
            string Username,
            string Email
        );
    }
}
