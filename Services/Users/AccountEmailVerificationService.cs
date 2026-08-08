using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Services.Administration;

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
                    string.IsNullOrWhiteSpace(fromName) ? ProductIdentity.DisplayName : fromName
                ),
                Subject = $"مرحباً بك في {ProductIdentity.DisplayName} | فعّل حسابك",
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = true,
                Body =
                    $"""
                    <!doctype html>
                    <html lang="ar" dir="rtl">
                    <body style="margin:0;padding:0;background:#f4f6f8;font-family:Tahoma,Arial,sans-serif;color:#172033">
                      <div style="display:none;max-height:0;overflow:hidden">
                        فعّل بريدك وابدأ تجربة تصاريح بخطوة واحدة.
                      </div>
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f6f8">
                        <tr>
                          <td align="center" style="padding:32px 16px">
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border:1px solid #e2e7ec;border-radius:10px;overflow:hidden">
                              <tr>
                                <td style="padding:22px 28px;background:#101820;color:#ffffff;font-size:20px;font-weight:700">
                                  {ProductIdentity.DisplayName}
                                </td>
                              </tr>
                              <tr>
                                <td style="padding:32px 28px;text-align:right">
                                  <h1 style="margin:0 0 14px;font-size:25px;line-height:1.5;color:#172033">
                                    أهلاً {safeName}
                                  </h1>
                                  <p style="margin:0 0 10px;font-size:17px;line-height:1.9">
                                    خطوتك الأولى نحو إدارة دخول أسهل وأكثر أماناً.
                                  </p>
                                  <p style="margin:0 0 24px;font-size:15px;line-height:1.8;color:#596579">
                                    فعّل بريدك الآن لتكمل إعداد حسابك وتبدأ استخدام المنصة.
                                  </p>
                                  <a href="{safeUrl}" style="display:inline-block;padding:13px 28px;background:#159a8c;color:#ffffff;text-decoration:none;border-radius:6px;font-size:16px;font-weight:700">
                                    تفعيل الحساب
                                  </a>
                                  <p style="margin:26px 0 0;font-size:13px;line-height:1.8;color:#7b8493">
                                    رابط التفعيل صالح لمدة 24 ساعة. إذا لم تطلب إنشاء الحساب، يمكنك تجاهل هذه الرسالة.
                                  </p>
                                </td>
                              </tr>
                            </table>
                          </td>
                        </tr>
                      </table>
                    </body>
                    </html>
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
