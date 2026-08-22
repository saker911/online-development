using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OtpNet;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Common;

namespace VehiclePermitSystemWeb.Services.Users;

public static class MultiFactorAuthenticationRequirement
{
    public const string AuthenticationMethodClaimType = "amr";
    public const string AuthenticationMethodClaimValue = "mfa";

    public static bool IsRequired(UserAccount? user, bool enabled = true) =>
        enabled
        && (
            user?.IsSuperAdmin == true
            || string.Equals(user?.Role, AppRoles.SystemAdmin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(user?.Role, AppRoles.GeneralManager, StringComparison.OrdinalIgnoreCase)
        );
}

public interface IMultiFactorAuthenticationService
{
    bool IsRequired(UserAccount? user);
    string BeginChallenge(UserAccount user, string? returnUrl);
    MfaChallengeView? GetChallenge(string challengeId);
    MfaVerificationResult Verify(string challengeId, string code);
}

public sealed record MfaChallengeView(
    string ChallengeId,
    string TenantId,
    string Username,
    string DisplayName,
    bool RequiresEnrollment,
    string ManualKey,
    string ProvisioningUri
);

public sealed record MfaVerificationResult(
    bool Succeeded,
    string Message,
    UserAccount? User = null,
    string? ReturnUrl = null,
    bool WasEnrollment = false,
    IReadOnlyList<string>? RecoveryCodes = null
);

public sealed class MultiFactorAuthenticationService : IMultiFactorAuthenticationService
{
    private const string Issuer = "Tasareeh";
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _secretProtector;
    private readonly ISystemClock _clock;
    private readonly ILogger<MultiFactorAuthenticationService> _logger;
    private readonly bool _requiredForPrivilegedAccounts;

    public MultiFactorAuthenticationService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IMemoryCache cache,
        IDataProtectionProvider dataProtectionProvider,
        ISystemClock clock,
        ILogger<MultiFactorAuthenticationService> logger,
        IConfiguration? configuration = null
    )
    {
        _dbContextFactory = dbContextFactory;
        _cache = cache;
        _secretProtector = dataProtectionProvider.CreateProtector(
            "VehiclePermitSystemWeb.UserMfaSecret.v1"
        );
        _clock = clock;
        _logger = logger;
        _requiredForPrivilegedAccounts = configuration?.GetValue(
            "Security:Mfa:RequiredForPrivilegedAccounts",
            true
        ) ?? true;
    }

    public bool IsRequired(UserAccount? user) =>
        MultiFactorAuthenticationRequirement.IsRequired(user, _requiredForPrivilegedAccounts);

    public string BeginChallenge(UserAccount user, string? returnUrl)
    {
        if (!IsRequired(user) || !user.IsActive)
        {
            throw new InvalidOperationException("MFA challenge is only available to active privileged accounts.");
        }

        var challengeId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var challenge = new PendingMfaChallenge
        {
            TenantId = user.TenantId,
            Username = user.Username,
            ReturnUrl = returnUrl ?? string.Empty,
            ExpiresAtUtc = _clock.UtcNow.Add(ChallengeLifetime),
        };
        _cache.Set(challengeId, challenge, challenge.ExpiresAtUtc);
        return challengeId;
    }

    public MfaChallengeView? GetChallenge(string challengeId)
    {
        if (!TryGetChallenge(challengeId, out var challenge))
        {
            return null;
        }

        using var db = _dbContextFactory.CreateDbContext();
        var user = FindUser(db, challenge.TenantId, challenge.Username);
        if (user == null || !user.IsActive || !IsRequired(user))
        {
            _cache.Remove(challengeId);
            return null;
        }

        if (user.MfaEnabled)
        {
            return new MfaChallengeView(
                challengeId,
                user.TenantId,
                user.Username,
                user.DisplayName,
                false,
                string.Empty,
                string.Empty
            );
        }

        lock (challenge.SyncRoot)
        {
            challenge.PendingSecret ??= KeyGeneration.GenerateRandomKey(20);
            var manualKey = Base32Encoding.ToString(challenge.PendingSecret);
            var accountLabel = $"{user.Username}@{user.TenantId}";
            var provisioningUri = BuildProvisioningUri(manualKey, accountLabel);
            return new MfaChallengeView(
                challengeId,
                user.TenantId,
                user.Username,
                user.DisplayName,
                true,
                manualKey,
                provisioningUri
            );
        }
    }

    public MfaVerificationResult Verify(string challengeId, string code)
    {
        if (!TryGetChallenge(challengeId, out var challenge))
        {
            return Failed("انتهت جلسة التحقق. سجل الدخول مرة أخرى.");
        }

        lock (challenge.SyncRoot)
        {
            challenge.Attempts++;
            if (challenge.Attempts > MaximumAttempts)
            {
                _cache.Remove(challengeId);
                return Failed("تم إيقاف جلسة التحقق بعد محاولات متعددة. سجل الدخول مرة أخرى.");
            }

            using var db = _dbContextFactory.CreateDbContext();
            var user = FindUser(db, challenge.TenantId, challenge.Username, tracking: true);
            if (user == null || !user.IsActive || !IsRequired(user))
            {
                _cache.Remove(challengeId);
                return Failed("تعذر إكمال التحقق لهذا الحساب.");
            }

            var normalizedCode = NormalizeCode(code);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return Failed("أدخل رمز التحقق.");
            }

            var wasEnrollment = !user.MfaEnabled;
            IReadOnlyList<string>? recoveryCodes = null;
            if (wasEnrollment)
            {
                if (challenge.PendingSecret == null)
                {
                    return Failed("أعد فتح صفحة إعداد تطبيق المصادقة.");
                }

                if (!TryVerifyTotp(challenge.PendingSecret, normalizedCode, out var matchedStep))
                {
                    return Failed("رمز التحقق غير صحيح.");
                }

                recoveryCodes = GenerateRecoveryCodes();
                user.MfaEnabled = true;
                user.MfaSecretProtected = _secretProtector.Protect(
                    Base32Encoding.ToString(challenge.PendingSecret)
                );
                user.MfaRecoveryCodeHashesJson = JsonSerializer.Serialize(
                    recoveryCodes.Select(HashRecoveryCode).ToArray()
                );
                user.MfaEnrolledAtUtc = _clock.UtcNow;
                user.MfaLastVerifiedStep = matchedStep;
            }
            else if (IsSixDigitCode(normalizedCode))
            {
                byte[] secret;
                try
                {
                    var protectedSecret = _secretProtector.Unprotect(user.MfaSecretProtected);
                    secret = Base32Encoding.ToBytes(protectedSecret);
                }
                catch (Exception exception) when (
                    exception is CryptographicException or FormatException or ArgumentException
                )
                {
                    _logger.LogError(
                        exception,
                        "Unable to decrypt MFA secret for tenant {TenantId}, user {Username}.",
                        user.TenantId,
                        user.Username
                    );
                    return Failed("تعذر التحقق من إعداد المصادقة. تواصل مع مسؤول النظام.");
                }

                if (!TryVerifyTotp(secret, normalizedCode, out var matchedStep))
                {
                    return Failed("رمز التحقق غير صحيح.");
                }

                if (user.MfaLastVerifiedStep.HasValue && matchedStep <= user.MfaLastVerifiedStep.Value)
                {
                    return Failed("تم استخدام هذا الرمز سابقًا. انتظر الرمز التالي.");
                }

                user.MfaLastVerifiedStep = matchedStep;
            }
            else if (!ConsumeRecoveryCode(user, normalizedCode))
            {
                return Failed("رمز التحقق أو الاسترداد غير صحيح.");
            }

            db.SaveChanges();
            _cache.Remove(challengeId);
            return new MfaVerificationResult(
                true,
                "تم التحقق بنجاح.",
                user,
                challenge.ReturnUrl,
                wasEnrollment,
                recoveryCodes
            );
        }
    }

    private bool TryGetChallenge(string challengeId, out PendingMfaChallenge challenge)
    {
        challenge = null!;
        var normalizedId = (challengeId ?? string.Empty).Trim();
        if (
            normalizedId.Length != 64
            || !_cache.TryGetValue(normalizedId, out PendingMfaChallenge? cached)
            || cached == null
        )
        {
            return false;
        }

        if (cached.ExpiresAtUtc <= _clock.UtcNow)
        {
            _cache.Remove(normalizedId);
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

        return query.SingleOrDefault(user =>
            user.TenantId == tenantId && user.Username == username
        );
    }

    private static bool TryVerifyTotp(byte[] secret, string code, out long matchedStep)
    {
        var totp = new Totp(secret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.VerifyTotp(
            code,
            out matchedStep,
            VerificationWindow.RfcSpecifiedNetworkDelay
        );
    }

    private static bool ConsumeRecoveryCode(UserAccount user, string candidate)
    {
        string[] storedHashes;
        try
        {
            storedHashes = JsonSerializer.Deserialize<string[]>(user.MfaRecoveryCodeHashesJson) ?? [];
        }
        catch (JsonException)
        {
            return false;
        }

        var candidateHash = Convert.FromBase64String(HashRecoveryCode(candidate));
        for (var index = 0; index < storedHashes.Length; index++)
        {
            byte[] storedHash;
            try
            {
                storedHash = Convert.FromBase64String(storedHashes[index]);
            }
            catch (FormatException)
            {
                continue;
            }

            if (!CryptographicOperations.FixedTimeEquals(candidateHash, storedHash))
            {
                continue;
            }

            user.MfaRecoveryCodeHashesJson = JsonSerializer.Serialize(
                storedHashes.Where((_, storedIndex) => storedIndex != index).ToArray()
            );
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> GenerateRecoveryCodes() =>
        Enumerable.Range(0, 8).Select(_ => FormatRecoveryCode(RandomNumberGenerator.GetBytes(8))).ToArray();

    private static string FormatRecoveryCode(byte[] bytes)
    {
        var raw = Base32Encoding.ToString(bytes).TrimEnd('=').ToUpperInvariant();
        return string.Join('-', raw.Chunk(4).Select(chunk => new string(chunk)));
    }

    private static string HashRecoveryCode(string code)
    {
        var normalized = NormalizeRecoveryCode(code);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string NormalizeCode(string code) =>
        new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string NormalizeRecoveryCode(string code) => NormalizeCode(code);

    private static bool IsSixDigitCode(string code) =>
        code.Length == 6 && code.All(char.IsAsciiDigit);

    private static string BuildProvisioningUri(string secret, string accountLabel)
    {
        var label = Uri.EscapeDataString($"{Issuer}:{accountLabel}");
        return $"otpauth://totp/{label}?secret={Uri.EscapeDataString(secret)}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";
    }

    private static MfaVerificationResult Failed(string message) => new(false, message);

    private sealed class PendingMfaChallenge
    {
        public object SyncRoot { get; } = new();
        public string TenantId { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string ReturnUrl { get; init; } = string.Empty;
        public DateTime ExpiresAtUtc { get; init; }
        public byte[]? PendingSecret { get; set; }
        public int Attempts { get; set; }
    }
}
