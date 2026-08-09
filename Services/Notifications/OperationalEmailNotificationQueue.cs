using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Utilities.Dates;

namespace VehiclePermitSystemWeb.Services.Notifications
{
    public interface IOperationalEmailNotificationQueue
    {
        bool QueueVisitDecision(ApplicationDbContext db, Visit visit);
        bool QueuePermitDecision(ApplicationDbContext db, Permit permit);
    }

    public sealed class OperationalEmailNotificationQueue
        : IOperationalEmailNotificationQueue
    {
        private readonly IConfiguration _configuration;
        private readonly ISystemClock _systemClock;
        private readonly ITimeLimitedDataProtector _publicVisitProtector;

        public OperationalEmailNotificationQueue(
            IConfiguration configuration,
            ISystemClock systemClock,
            IDataProtectionProvider dataProtectionProvider
        )
        {
            _configuration = configuration;
            _systemClock = systemClock;
            _publicVisitProtector = dataProtectionProvider
                .CreateProtector("VehiclePermitSystem.PublicVisitStatus.v1")
                .ToTimeLimitedDataProtector();
        }

        public bool QueueVisitDecision(ApplicationDbContext db, Visit visit)
        {
            if (!IsFinalDecision(visit.ApprovalStatus))
            {
                return false;
            }

            var email = NormalizeEmail(visit.VisitorEmail);
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            var approved = string.Equals(
                visit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            );
            var organization = ResolveOrganizationName(db, visit.TenantId);
            var tenantSlug = db
                .Tenants.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(tenant => tenant.TenantId == visit.TenantId)
                .Select(tenant => tenant.Slug)
                .FirstOrDefault();
            tenantSlug = string.IsNullOrWhiteSpace(tenantSlug) ? visit.TenantId : tenantSlug;
            var token = _publicVisitProtector.Protect(
                $"{visit.TenantId}|{visit.VisitId}",
                TimeSpan.FromDays(30)
            );
            var statusUrl = BuildPublicUrl(
                $"/o/{Uri.EscapeDataString(tenantSlug)}/visit-request/status?token={Uri.EscapeDataString(token)}"
            );
            var title = approved ? "تم اعتماد طلب زيارتك" : "تم رفض طلب زيارتك";
            var summary = approved
                ? "طلبك جاهز. افتح بطاقة الزيارة قبل الوصول وقدم رمز الدخول عند البوابة."
                : "تعذر اعتماد الطلب في هذه المرة. يمكنك التواصل مع الجهة لمزيد من التفاصيل.";
            var details = new[]
            {
                ("رقم الطلب", visit.VisitId),
                ("الجهة", organization),
                ("موعد الزيارة", HijriDateFormatter.Format(visit.VisitDate)),
                ("مكان الزيارة", visit.VisitLocation),
            };

            return Enqueue(
                db,
                visit.TenantId,
                approved ? "VisitApproved" : "VisitRejected",
                "Visit",
                visit.VisitId,
                email,
                visit.VisitorName,
                $"{title} | {organization}",
                BuildHtmlBody(
                    visit.VisitorName,
                    title,
                    summary,
                    details,
                    "متابعة الطلب",
                    statusUrl
                ),
                BuildTextBody(title, summary, details, statusUrl)
            );
        }

        public bool QueuePermitDecision(ApplicationDbContext db, Permit permit)
        {
            if (!IsFinalDecision(permit.ApprovalStatus))
            {
                return false;
            }

            var email = NormalizeEmail(permit.HolderEmail);
            if (string.IsNullOrWhiteSpace(email))
            {
                var candidateKeys = new[] { permit.NationalId, permit.EmployeeNumber }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                email = db
                    .UserAccounts.AsNoTracking()
                    .Where(user =>
                        user.IsActive
                        && user.IsEmailConfirmed
                        && candidateKeys.Contains(user.Username)
                    )
                    .Select(user => user.Email)
                    .AsEnumerable()
                    .Select(NormalizeEmail)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            var approved = string.Equals(
                permit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            );
            var organization = ResolveOrganizationName(db, permit.TenantId);
            var tenantSlug = db
                .Tenants.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(tenant => tenant.TenantId == permit.TenantId)
                .Select(tenant => tenant.Slug)
                .FirstOrDefault();
            tenantSlug = string.IsNullOrWhiteSpace(tenantSlug) ? permit.TenantId : tenantSlug;
            var passUrl = approved && !string.IsNullOrWhiteSpace(permit.QrToken)
                ? BuildPublicUrl(
                    $"/o/{Uri.EscapeDataString(tenantSlug)}/pass/{Uri.EscapeDataString(permit.QrToken)}"
                )
                : string.Empty;
            var title = approved ? "تم اعتماد تصريحك" : "تم رفض طلب التصريح";
            var summary = approved
                ? "تصريحك أصبح فعالاً. افتح البطاقة الرقمية وقدم رمزها عند البوابة."
                : "تعذر اعتماد الطلب في هذه المرة. راجع مسؤول التصاريح في جهتك لمزيد من التفاصيل.";
            var details = new[]
            {
                ("رقم التصريح", permit.PublicPermitCode),
                ("الجهة", organization),
                ("نوع التصريح", permit.PermitTypeDisplay),
                ("الموقع", permit.LocationDisplay),
            };

            return Enqueue(
                db,
                permit.TenantId,
                approved ? "PermitApproved" : "PermitRejected",
                "Permit",
                permit.PermitNumber,
                email,
                permit.DriverName,
                $"{title} | {organization}",
                BuildHtmlBody(
                    permit.DriverName,
                    title,
                    summary,
                    details,
                    approved ? "فتح التصريح الرقمي" : string.Empty,
                    passUrl
                ),
                BuildTextBody(title, summary, details, passUrl)
            );
        }

        private bool Enqueue(
            ApplicationDbContext db,
            string tenantId,
            string notificationType,
            string referenceType,
            string referenceId,
            string recipientEmail,
            string recipientName,
            string subject,
            string htmlBody,
            string textBody
        )
        {
            var deduplicationKey = string.Join(
                ':',
                notificationType.ToLowerInvariant(),
                referenceId.Trim().ToLowerInvariant(),
                recipientEmail.ToLowerInvariant()
            );
            if (
                db.EmailNotificationOutbox.Any(item =>
                    item.TenantId == tenantId
                    && item.DeduplicationKey == deduplicationKey
                )
            )
            {
                return false;
            }

            var now = _systemClock.UtcNow;
            db.EmailNotificationOutbox.Add(
                new EmailNotificationOutbox
                {
                    TenantId = tenantId,
                    NotificationType = notificationType,
                    ReferenceType = referenceType,
                    ReferenceId = referenceId,
                    DeduplicationKey = deduplicationKey,
                    RecipientEmail = recipientEmail,
                    RecipientName = recipientName.Trim(),
                    Subject = subject,
                    HtmlBody = htmlBody,
                    TextBody = textBody,
                    Status = EmailNotificationOutbox.StatusPending,
                    CreatedAtUtc = now,
                    NextAttemptAtUtc = now,
                }
            );
            return true;
        }

        private string ResolveOrganizationName(ApplicationDbContext db, string tenantId)
        {
            var administrationName = db
                .AdministrationSettings.AsNoTracking()
                .Where(settings => settings.TenantId == tenantId)
                .Select(settings => settings.OrganizationName)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(administrationName))
            {
                return administrationName.Trim();
            }

            return db
                    .Tenants.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(tenant => tenant.TenantId == tenantId)
                    .Select(tenant => tenant.Name)
                    .FirstOrDefault()
                ?? ProductIdentity.DisplayName;
        }

        private string BuildPublicUrl(string path)
        {
            var configuredBaseUrl = _configuration["App:PublicBaseUrl"]?.Trim();
            return string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? path
                : $"{configuredBaseUrl.TrimEnd('/')}{path}";
        }

        private static string? NormalizeEmail(string? value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            try
            {
                var address = new MailAddress(normalized);
                return string.Equals(
                    address.Address,
                    normalized,
                    StringComparison.OrdinalIgnoreCase
                )
                    ? normalized
                    : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static bool IsFinalDecision(string? status) =>
            string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase);

        private static string BuildHtmlBody(
            string recipientName,
            string title,
            string summary,
            IEnumerable<(string Label, string Value)> details,
            string actionLabel,
            string actionUrl
        )
        {
            static string Encode(string value) => HtmlEncoder.Default.Encode(value);
            var detailRows = string.Join(
                string.Empty,
                details
                    .Where(detail => !string.IsNullOrWhiteSpace(detail.Value))
                    .Select(detail =>
                        $"<tr><td style=\"padding:9px 0;color:#667085\">{Encode(detail.Label)}</td><td style=\"padding:9px 0;font-weight:700;color:#172033\">{Encode(detail.Value)}</td></tr>"
                    )
            );
            var action = string.IsNullOrWhiteSpace(actionUrl)
                ? string.Empty
                : $"<a href=\"{Encode(actionUrl)}\" style=\"display:inline-block;margin-top:22px;padding:13px 24px;background:#159a8c;color:#fff;text-decoration:none;border-radius:6px;font-weight:700\">{Encode(actionLabel)}</a>";

            return $"""
                <!doctype html>
                <html lang="ar" dir="rtl">
                <body style="margin:0;padding:0;background:#f4f6f8;font-family:Tahoma,Arial,sans-serif;color:#172033">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f6f8">
                    <tr><td align="center" style="padding:28px 14px">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:580px;background:#fff;border:1px solid #e2e7ec;border-radius:8px;overflow:hidden">
                        <tr><td style="padding:20px 26px;background:#101820;color:#fff;font-size:19px;font-weight:700">{Encode(ProductIdentity.DisplayName)}</td></tr>
                        <tr><td style="padding:30px 26px;text-align:right">
                          <p style="margin:0 0 8px;color:#667085">أهلاً {Encode(recipientName)}</p>
                          <h1 style="margin:0 0 12px;font-size:25px;line-height:1.5">{Encode(title)}</h1>
                          <p style="margin:0 0 20px;font-size:15px;line-height:1.9;color:#475467">{Encode(summary)}</p>
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="border-top:1px solid #e5e7eb;border-bottom:1px solid #e5e7eb">{detailRows}</table>
                          {action}
                          <p style="margin:24px 0 0;font-size:12px;line-height:1.8;color:#98a2b3">هذه رسالة آلية مرتبطة بطلبك في المنصة.</p>
                        </td></tr>
                      </table>
                    </td></tr>
                  </table>
                </body>
                </html>
                """;
        }

        private static string BuildTextBody(
            string title,
            string summary,
            IEnumerable<(string Label, string Value)> details,
            string actionUrl
        )
        {
            var lines = new List<string> { title, summary, string.Empty };
            lines.AddRange(
                details
                    .Where(detail => !string.IsNullOrWhiteSpace(detail.Value))
                    .Select(detail => $"{detail.Label}: {detail.Value}")
            );
            if (!string.IsNullOrWhiteSpace(actionUrl))
            {
                lines.Add(string.Empty);
                lines.Add(actionUrl);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
