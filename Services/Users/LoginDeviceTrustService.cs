using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Common;

namespace VehiclePermitSystemWeb.Services.Users;

public interface ILoginDeviceTrustService
{
    string GetCookieName(UserAccount user);
    bool IsTrusted(UserAccount user, string? token);
    DeviceChallengeStartResult BeginChallenge(UserAccount user, string? returnUrl, string? userAgent);
    DeviceChallengeStartResult ResendChallenge(string challengeId);
    DeviceChallengeView? GetChallenge(string challengeId);
    DeviceChallengeVerificationResult Verify(string challengeId, string code);
}

public sealed record DeviceChallengeStartResult(bool Succeeded, string Message, string ChallengeId = "");
public sealed record DeviceChallengeView(string ChallengeId, string MaskedEmail);
public sealed record DeviceChallengeVerificationResult(
    bool Succeeded,
    string Message,
    UserAccount? User = null,
    string? ReturnUrl = null,
    string? DeviceToken = null
);

public sealed class LoginDeviceTrustService : ILoginDeviceTrustService
{
    private const int MaximumAttempts = 5;
    private const int MaximumResends = 3;
    private static readonly TimeSpan ResendInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TrustedDeviceLifetime = TimeSpan.FromDays(90);
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IMemoryCache _cache;
    private readonly ISystemClock _clock;
    private readonly ILogger<LoginDeviceTrustService> _logger;

    public LoginDeviceTrustService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IMemoryCache cache,
        ISystemClock clock,
        ILogger<LoginDeviceTrustService> logger
    )
    {
        _dbContextFactory = dbContextFactory;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    public string GetCookieName(UserAccount user)
    {
        var accountKey = $"{user.TenantId}\n{user.Username}";
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountKey)))[..16]
            .ToLowerInvariant();
        return $"tasareeh_device_{suffix}";
    }

    public bool IsTrusted(UserAccount user, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var tokenHash = HashToken(token);
        var now = _clock.UtcNow;
        using var db = _dbContextFactory.CreateDbContext();
        var device = db.TrustedLoginDevices.IgnoreQueryFilters().SingleOrDefault(item =>
            item.TenantId == user.TenantId
            && item.Username == user.Username
            && item.TokenHash == tokenHash
            && item.RevokedAtUtc == null
            && item.ExpiresAtUtc > now
        );
        if (device == null)
        {
            return false;
        }

        if (device.LastUsedAtUtc < now.AddHours(-12))
        {
            device.LastUsedAtUtc = now;
            db.SaveChanges();
        }
        return true;
    }

    public DeviceChallengeStartResult BeginChallenge(
        UserAccount user,
        string? returnUrl,
        string? userAgent
    )
    {
        if (!user.IsActive || !user.IsEmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            return new(false, "لا يوجد بريد إلكتروني مؤكد لهذا الحساب.");
        }

        var accountChallengeKey = BuildAccountChallengeKey(user.TenantId, user.Username);
        if (
            _cache.TryGetValue(accountChallengeKey, out string? activeChallengeId)
            && !string.IsNullOrWhiteSpace(activeChallengeId)
            && GetChallenge(activeChallengeId) != null
        )
        {
            return new(true, "رمز التحقق السابق ما زال صالحاً.", activeChallengeId);
        }

        var challengeId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var now = _clock.UtcNow;
        var challenge = new PendingDeviceChallenge
        {
            TenantId = user.TenantId,
            Username = user.Username,
            ReturnUrl = returnUrl ?? string.Empty,
            CodeHash = HashCode(challengeId, code),
            DeviceDescription = DescribeDevice(userAgent),
            ExpiresAtUtc = now.Add(ChallengeLifetime),
            LastSentAtUtc = now,
        };

        try
        {
            using var db = _dbContextFactory.CreateDbContext();
            var safeName = WebUtility.HtmlEncode(
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName
            );
            var notification = new EmailNotificationOutbox
                {
                    TenantId = user.TenantId,
                    NotificationType = "NewDeviceVerification",
                    ReferenceType = "UserAccount",
                    ReferenceId = user.Username,
                    DeduplicationKey = $"new-device:{challengeId}",
                    RecipientEmail = user.Email,
                    RecipientName = user.DisplayName,
                    Subject = "رمز تأكيد تسجيل الدخول | تصاريح",
                    HtmlBody = BuildHtmlBody(safeName, code, challenge.DeviceDescription),
                    TextBody = BuildTextBody(user.DisplayName, code, challenge.DeviceDescription),
                    Status = EmailNotificationOutbox.StatusPending,
                    CreatedAtUtc = now,
                    NextAttemptAtUtc = now,
                };
            db.EmailNotificationOutbox.Add(notification);
            db.SaveChanges();
            challenge.NotificationId = notification.Id;
            _cache.Set(challengeId, challenge, challenge.ExpiresAtUtc);
            _cache.Set(accountChallengeKey, challengeId, challenge.ExpiresAtUtc);
            return new(true, "تم طلب إرسال رمز التحقق إلى البريد المسجل.", challengeId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to queue new-device verification email.");
            return new(false, "تعذر إرسال رمز التحقق حالياً. حاول مرة أخرى.");
        }
    }

    public DeviceChallengeStartResult ResendChallenge(string challengeId)
    {
        if (!TryGetChallenge(challengeId, out var challenge))
            return new(false, "انتهت جلسة التحقق. سجل الدخول مرة أخرى.");

        lock (challenge.SyncRoot)
        {
            if (!TryGetChallenge(challengeId, out _) || challenge.Attempts >= MaximumAttempts)
                return new(false, "انتهت جلسة التحقق. سجل الدخول مرة أخرى.");
            var now = _clock.UtcNow;
            if (now - challenge.LastSentAtUtc < ResendInterval)
                return new(false, "انتظر دقيقة بين طلبات إرسال الرمز.", challengeId);
            if (challenge.ResendCount >= MaximumResends)
                return new(false, "بلغت الحد المتاح لإعادة الإرسال. حاول تسجيل الدخول بعد انتهاء الجلسة.", challengeId);

            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var user = FindUser(db, challenge.TenantId, challenge.Username);
                if (user == null || !user.IsActive || !user.IsEmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
                    return new(false, "تعذر إرسال رمز التحقق لهذا الحساب.", challengeId);

                // Resending rotates the code without resetting the attempt limit or session expiry.
                string code;
                byte[] codeHash;
                do
                {
                    code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                    codeHash = HashCode(challengeId, code);
                } while (CryptographicOperations.FixedTimeEquals(codeHash, challenge.CodeHash));
                var previous = db.EmailNotificationOutbox.IgnoreQueryFilters()
                    .SingleOrDefault(message => message.Id == challenge.NotificationId);
                if (previous?.Status == EmailNotificationOutbox.StatusPending)
                {
                    previous.Status = EmailNotificationOutbox.StatusFailed;
                    previous.LastError = "تم استبدال رمز التحقق بطلب جديد.";
                }

                var minutes = Math.Max(1, (int)Math.Ceiling((challenge.ExpiresAtUtc - now).TotalMinutes));
                var notification = new EmailNotificationOutbox
                {
                    TenantId = user.TenantId,
                    NotificationType = "NewDeviceVerification",
                    ReferenceType = "UserAccount",
                    ReferenceId = user.Username,
                    DeduplicationKey = $"new-device:{challengeId}:resend:{challenge.ResendCount + 1}",
                    RecipientEmail = user.Email,
                    RecipientName = user.DisplayName,
                    Subject = "رمز تأكيد تسجيل الدخول | تصاريح",
                    HtmlBody = BuildHtmlBody(WebUtility.HtmlEncode(user.DisplayName), code, challenge.DeviceDescription, minutes),
                    TextBody = BuildTextBody(user.DisplayName, code, challenge.DeviceDescription, minutes),
                    CreatedAtUtc = now,
                    NextAttemptAtUtc = now,
                    Status = EmailNotificationOutbox.StatusPending,
                };
                db.EmailNotificationOutbox.Add(notification);
                db.SaveChanges();
                challenge.CodeHash = codeHash;
                challenge.NotificationId = notification.Id;
                challenge.LastSentAtUtc = now;
                challenge.ResendCount++;
                return new(true, "تم طلب إرسال رمز جديد. استخدم أحدث رسالة، وتحقق من البريد غير المرغوب فيه.", challengeId);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to requeue new-device verification email.");
                return new(false, "تعذر إرسال رمز التحقق حالياً. حاول مرة أخرى.", challengeId);
            }
        }
    }

    public DeviceChallengeView? GetChallenge(string challengeId)
    {
        if (!TryGetChallenge(challengeId, out var challenge))
        {
            return null;
        }

        using var db = _dbContextFactory.CreateDbContext();
        var user = FindUser(db, challenge.TenantId, challenge.Username);
        return user == null ? null : new(challengeId, MaskEmail(user.Email));
    }

    public DeviceChallengeVerificationResult Verify(string challengeId, string code)
    {
        if (!TryGetChallenge(challengeId, out var challenge))
        {
            return new(false, "انتهت جلسة التحقق. سجل الدخول مرة أخرى.");
        }

        lock (challenge.SyncRoot)
        {
            if (!TryGetChallenge(challengeId, out _))
                return new(false, "انتهت جلسة التحقق. سجل الدخول مرة أخرى.");
            challenge.Attempts++;
            if (challenge.Attempts > MaximumAttempts)
            {
                _cache.Remove(challengeId);
                return new(false, "تم إيقاف التحقق بعد محاولات متعددة. سجل الدخول مرة أخرى.");
            }

            var normalizedCode = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
            var submittedHash = HashCode(challengeId, normalizedCode);
            if (!CryptographicOperations.FixedTimeEquals(submittedHash, challenge.CodeHash))
            {
                return new(false, "رمز التحقق غير صحيح.");
            }

            using var db = _dbContextFactory.CreateDbContext();
            var user = FindUser(db, challenge.TenantId, challenge.Username, tracking: true);
            if (user == null || !user.IsActive)
            {
                _cache.Remove(challengeId);
                return new(false, "تعذر إكمال التحقق لهذا الحساب.");
            }

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var now = _clock.UtcNow;
            db.TrustedLoginDevices.Add(
                new TrustedLoginDevice
                {
                    TenantId = user.TenantId,
                    Username = user.Username,
                    TokenHash = HashToken(token),
                    DeviceDescription = challenge.DeviceDescription,
                    CreatedAtUtc = now,
                    LastUsedAtUtc = now,
                    ExpiresAtUtc = now.Add(TrustedDeviceLifetime),
                }
            );
            db.SaveChanges();
            _cache.Remove(challengeId);
            _cache.Remove(BuildAccountChallengeKey(challenge.TenantId, challenge.Username));
            return new(true, "تم تأكيد الجهاز.", user, challenge.ReturnUrl, token);
        }
    }

    private bool TryGetChallenge(string challengeId, out PendingDeviceChallenge challenge)
    {
        challenge = null!;
        var id = (challengeId ?? string.Empty).Trim();
        if (id.Length != 64 || !_cache.TryGetValue(id, out PendingDeviceChallenge? cached) || cached == null)
        {
            return false;
        }
        if (cached.ExpiresAtUtc <= _clock.UtcNow)
        {
            _cache.Remove(id);
            return false;
        }
        challenge = cached;
        return true;
    }

    private static UserAccount? FindUser(
        ApplicationDbContext db,
        string tenantId,
        string username,
        bool tracking = false
    )
    {
        var query = db.UserAccounts.IgnoreQueryFilters();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }
        return query.SingleOrDefault(user => user.TenantId == tenantId && user.Username == username);
    }

    private static byte[] HashCode(string challengeId, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId}:{code}"));

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string BuildAccountChallengeKey(string tenantId, string username) =>
        $"login-device-challenge:{tenantId}:{username}";

    private static string MaskEmail(string email)
    {
        var parts = (email ?? string.Empty).Split('@', 2);
        if (parts.Length != 2 || parts[0].Length == 0)
        {
            return "البريد المسجل";
        }
        var visible = parts[0][..Math.Min(2, parts[0].Length)];
        return $"{visible}***@{parts[1]}";
    }

    private static string DescribeDevice(string? userAgent)
    {
        var value = userAgent ?? string.Empty;
        var browser = value.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Microsoft Edge"
            : value.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Google Chrome"
            : value.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : value.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : "متصفح جديد";
        var device = value.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ? "جوال"
            : value.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ? "جهاز لوحي"
            : "كمبيوتر";
        return $"{browser} على {device}";
    }

    private static string BuildHtmlBody(string name, string code, string device, int minutes = 10) =>
        $"""
        <!doctype html><html lang="ar" dir="rtl"><body style="margin:0;padding:0;background:#f4f6f8;font-family:Tahoma,Arial,sans-serif;color:#172033">
        <table role="presentation" width="100%" cellspacing="0" cellpadding="0"><tr><td align="center" style="padding:32px 16px">
        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#fff;border:1px solid #e2e7ec;border-radius:10px;overflow:hidden">
        <tr><td style="padding:22px 28px;background:#101820;color:#fff;font-size:20px;font-weight:700">تصاريح</td></tr>
        <tr><td style="padding:32px 28px;text-align:right"><h1 style="margin:0 0 12px;font-size:23px">تأكيد تسجيل الدخول</h1>
        <p style="line-height:1.9;color:#4b5563">مرحباً {name}، رصدنا محاولة دخول من {WebUtility.HtmlEncode(device)}. استخدم الرمز التالي إذا كنت أنت من بدأ العملية:</p>
        <div dir="ltr" style="margin:22px 0;padding:16px;text-align:center;background:#f3f7f6;border:1px solid #d6e7e2;border-radius:8px;font-size:30px;font-weight:700;letter-spacing:6px">{code}</div>
        <p style="font-size:13px;line-height:1.8;color:#7b8493">استخدم أحدث رمز خلال {minutes} دقائق. إذا لم تكن أنت، تجاهل الرسالة وغيّر كلمة المرور.</p></td></tr></table>
        </td></tr></table></body></html>
        """;

    private static string BuildTextBody(string name, string code, string device, int minutes = 10) =>
        $"مرحباً {name}\n\nرصدنا محاولة دخول من {device}. رمز التأكيد: {code}\n\nاستخدم أحدث رمز خلال {minutes} دقائق. إذا لم تكن أنت، تجاهل الرسالة وغيّر كلمة المرور.";

    private sealed class PendingDeviceChallenge
    {
        public string TenantId { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string ReturnUrl { get; init; } = string.Empty;
        public byte[] CodeHash { get; set; } = Array.Empty<byte>();
        public long NotificationId { get; set; }
        public DateTime LastSentAtUtc { get; set; }
        public int ResendCount { get; set; }
        public string DeviceDescription { get; init; } = string.Empty;
        public DateTime ExpiresAtUtc { get; init; }
        public int Attempts { get; set; }
        public object SyncRoot { get; } = new();
    }
}
