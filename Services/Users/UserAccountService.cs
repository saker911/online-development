using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.DTOs;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Account;
using VehiclePermitSystemWeb.Models.ViewModels.Backup;
using VehiclePermitSystemWeb.Models.ViewModels.Delegations;
using VehiclePermitSystemWeb.Models.ViewModels.Departments;
using VehiclePermitSystemWeb.Models.ViewModels.Display;
using VehiclePermitSystemWeb.Models.ViewModels.Permits;
using VehiclePermitSystemWeb.Models.ViewModels.Reports;
using VehiclePermitSystemWeb.Models.ViewModels.Scan;
using VehiclePermitSystemWeb.Models.ViewModels.Users;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;
using VehiclePermitSystemWeb.Security;

namespace VehiclePermitSystemWeb.Services.Users
{
    public static class UserAccountService
    {
        private const string PasswordHasherMarker = "ASP.NET Core Identity PasswordHasher";
        private static readonly PasswordHasher<UserAccount> PasswordHasher = new();

        public static string GenerateTemporaryPassword()
        {
            var number = RandomNumberGenerator.GetInt32(100000, 1000000);
            return $"Aa{number}!";
        }

        public static string GenerateTemporaryOperatorPin()
        {
            return RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");
        }

        public static UserAccount BuildInitialSystemOwner(
            string username,
            string fullName,
            string phoneNumber,
            string jobTitle,
            bool mustChangePassword
        )
        {
            var user = new UserAccount
            {
                Username = username,
                DisplayName = fullName,
                FullName = fullName,
                Department = string.Empty,
                JobTitle = string.IsNullOrWhiteSpace(jobTitle) ? "مشرف النظام" : jobTitle,
                PhoneNumber = phoneNumber,
                Email = string.Empty,
                IsActive = true,
                IsSuperAdmin = true,
                MustChangePassword = mustChangePassword,
                Role = AppRoles.SystemAdmin,
                ManagerUsername = string.Empty,
            };

            AppPermissions.ApplyRoleDefaults(user);
            return user;
        }

        public static string NormalizeOperatorBadgeCode(string? badgeCode)
        {
            return (badgeCode ?? string.Empty).Trim().ToUpperInvariant();
        }

        public static string BuildDefaultOperatorBadgeCode(string username)
        {
            var normalizedUsername = new string(
                (username ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant()
                    .Where(character => char.IsLetterOrDigit(character))
                    .ToArray()
            );
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                normalizedUsername = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            }

            return $"GATE-{normalizedUsername}";
        }

        public static string EnsureUniqueOperatorBadgeCode(
            IEnumerable<UserAccount> userAccounts,
            string username,
            string desiredCode
        )
        {
            var normalizedCode = NormalizeOperatorBadgeCode(desiredCode);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                normalizedCode = BuildDefaultOperatorBadgeCode(username);
            }

            var uniqueCode = normalizedCode;
            var suffix = 1;
            while (
                userAccounts.Any(user =>
                    user.Username != username && user.OperatorBadgeCode == uniqueCode
                )
            )
            {
                uniqueCode = $"{normalizedCode}-{suffix++}";
            }

            return uniqueCode;
        }

        public static bool IsOperatorPinValid(string pin)
        {
            return pin.Length == 6 && pin.All(char.IsDigit);
        }

        public static void SetOperatorPin(UserAccount user, string pin)
        {
            user.OperatorPinSalt = PasswordHasherMarker;
            user.OperatorPinHash = PasswordHasher.HashPassword(user, pin);
        }

        public static bool VerifyOperatorPin(UserAccount user, string pin)
        {
            if (
                string.IsNullOrWhiteSpace(user.OperatorPinHash)
                || string.IsNullOrWhiteSpace(user.OperatorPinSalt)
            )
            {
                return false;
            }

            if (string.Equals(user.OperatorPinSalt, PasswordHasherMarker, StringComparison.Ordinal))
            {
                var result = PasswordHasher.VerifyHashedPassword(user, user.OperatorPinHash, pin);
                if (result == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    SetOperatorPin(user, pin);
                }

                return result != PasswordVerificationResult.Failed;
            }

            var computedHash = HashPasswordLegacyCurrent(pin, user.OperatorPinSalt);
            if (string.Equals(user.OperatorPinHash, computedHash, StringComparison.Ordinal))
            {
                SetOperatorPin(user, pin);
                return true;
            }

            var legacyHash = HashPasswordLegacy(pin, user.OperatorPinSalt);
            if (string.Equals(user.OperatorPinHash, legacyHash, StringComparison.Ordinal))
            {
                SetOperatorPin(user, pin);
                return true;
            }

            return false;
        }

        public static string NormalizeDeviceId(string? deviceId)
        {
            return string.IsNullOrWhiteSpace(deviceId)
                ? "display-default"
                : deviceId.Trim().ToLowerInvariant();
        }

        public static string EnsureOperatorBadgeCode(
            ApplicationDbContext db,
            string username,
            string? preferredBadgeCode = null,
            bool ignoreTenantFilters = false
        )
        {
            var userAccounts = ignoreTenantFilters
                ? db.UserAccounts.IgnoreQueryFilters()
                : db.UserAccounts;
            var existing = userAccounts.FirstOrDefault(u => u.Username == username);
            if (existing == null)
            {
                return string.Empty;
            }

            var desiredCode = NormalizeOperatorBadgeCode(preferredBadgeCode);
            if (string.IsNullOrWhiteSpace(desiredCode))
            {
                desiredCode = NormalizeOperatorBadgeCode(existing.OperatorBadgeCode);
            }

            if (string.IsNullOrWhiteSpace(desiredCode))
            {
                desiredCode = BuildDefaultOperatorBadgeCode(existing.Username);
            }

            desiredCode = EnsureUniqueOperatorBadgeCode(
                userAccounts.AsNoTracking().ToList(),
                existing.Username,
                desiredCode
            );
            if (!string.Equals(existing.OperatorBadgeCode, desiredCode, StringComparison.Ordinal))
            {
                existing.OperatorBadgeCode = desiredCode;
                db.SaveChanges();
            }

            return desiredCode;
        }

        public static string? ConfigureOperatorCredentials(
            ApplicationDbContext db,
            string username,
            string badgeCode,
            string? temporaryPin,
            bool requirePinChange,
            bool ignoreTenantFilters = false
        )
        {
            var userAccounts = ignoreTenantFilters
                ? db.UserAccounts.IgnoreQueryFilters()
                : db.UserAccounts;
            var existing = userAccounts.FirstOrDefault(u => u.Username == username);
            if (existing == null)
            {
                return null;
            }

            var normalizedBadge = EnsureUniqueOperatorBadgeCode(
                userAccounts.AsNoTracking().ToList(),
                existing.Username,
                string.IsNullOrWhiteSpace(badgeCode)
                    ? BuildDefaultOperatorBadgeCode(existing.Username)
                    : NormalizeOperatorBadgeCode(badgeCode)
            );
            existing.OperatorBadgeCode = normalizedBadge;

            var effectivePin = string.IsNullOrWhiteSpace(temporaryPin)
                ? string.Empty
                : temporaryPin.Trim();
            if (!string.IsNullOrWhiteSpace(effectivePin) && !IsOperatorPinValid(effectivePin))
            {
                effectivePin = string.Empty;
            }
            if (
                string.IsNullOrWhiteSpace(effectivePin)
                && existing.CanScanOperations
                && string.IsNullOrWhiteSpace(existing.OperatorPinHash)
            )
            {
                effectivePin = GenerateTemporaryOperatorPin();
                requirePinChange = true;
            }

            if (!string.IsNullOrWhiteSpace(effectivePin))
            {
                SetOperatorPin(existing, effectivePin);
                existing.MustChangeOperatorPin = requirePinChange;
            }
            else if (requirePinChange && !string.IsNullOrWhiteSpace(existing.OperatorPinHash))
            {
                existing.MustChangeOperatorPin = true;
            }

            db.SaveChanges();
            return string.IsNullOrWhiteSpace(effectivePin) ? null : effectivePin;
        }

        public static void SetPassword(UserAccount user, string password)
        {
            user.PasswordSalt = PasswordHasherMarker;
            user.PasswordHash = PasswordHasher.HashPassword(user, password);
        }

        public static bool VerifyPassword(UserAccount user, string password, out bool needsUpgrade)
        {
            needsUpgrade = false;

            if (!string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                if (
                    string.Equals(user.PasswordSalt, PasswordHasherMarker, StringComparison.Ordinal)
                )
                {
                    var result = PasswordHasher.VerifyHashedPassword(
                        user,
                        user.PasswordHash,
                        password
                    );
                    needsUpgrade = result == PasswordVerificationResult.SuccessRehashNeeded;
                    return result != PasswordVerificationResult.Failed;
                }

                if (!string.IsNullOrWhiteSpace(user.PasswordSalt))
                {
                    var computedHash = HashPasswordLegacyCurrent(password, user.PasswordSalt);
                    if (string.Equals(user.PasswordHash, computedHash, StringComparison.Ordinal))
                    {
                        needsUpgrade = true;
                        return true;
                    }

                    var legacyHash = HashPasswordLegacy(password, user.PasswordSalt);
                    if (string.Equals(user.PasswordHash, legacyHash, StringComparison.Ordinal))
                    {
                        needsUpgrade = true;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool ChangePassword(
            ApplicationDbContext db,
            string username,
            string currentPassword,
            string newPassword
        )
        {
            var existing = db.UserAccounts.FirstOrDefault(u => u.Username == username);
            if (existing == null || !existing.IsActive)
            {
                return false;
            }

            if (!VerifyPassword(existing, currentPassword, out _))
            {
                return false;
            }

            if (!PasswordValidationRules.IsStrongPassword(newPassword))
            {
                return false;
            }

            SetPassword(existing, newPassword);
            existing.MustChangePassword = false;
            db.SaveChanges();
            return true;
        }

        public static bool ValidateCredentials(
            ApplicationDbContext db,
            string username,
            string password,
            out string displayName,
            out string role
        )
        {
            displayName = string.Empty;
            role = string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            if (!IsCurrentTenantAvailableForLogin(db))
            {
                return false;
            }

            var user = db.UserAccounts.FirstOrDefault(u => u.Username == username);
            if (user == null || !user.IsActive)
            {
                return false;
            }

            if (!VerifyPassword(user, password, out var needsUpgrade))
            {
                return false;
            }

            if (
                needsUpgrade
                || string.IsNullOrWhiteSpace(user.PasswordHash)
                || string.IsNullOrWhiteSpace(user.PasswordSalt)
            )
            {
                SetPassword(user, password);
                db.SaveChanges();
            }

            displayName = user.DisplayName;
            role = user.Role;
            return true;
        }

        private static bool IsCurrentTenantAvailableForLogin(ApplicationDbContext db)
        {
            var tenant = db.Tenants.AsNoTracking().FirstOrDefault(item =>
                item.TenantId == db.CurrentTenantId
            );
            if (tenant == null || !tenant.IsActive)
            {
                return false;
            }

            var status = TenantSubscriptionStatuses.Normalize(tenant.SubscriptionStatus);
            if (
                string.Equals(status, TenantSubscriptionStatuses.PendingPayment, StringComparison.Ordinal)
                || string.Equals(status, TenantSubscriptionStatuses.Suspended, StringComparison.Ordinal)
                || string.Equals(status, TenantSubscriptionStatuses.Expired, StringComparison.Ordinal)
            )
            {
                return false;
            }

            var now = DateTime.UtcNow;
            if (
                string.Equals(status, TenantSubscriptionStatuses.Trial, StringComparison.Ordinal)
                && tenant.TrialEndsAtUtc.HasValue
                && tenant.TrialEndsAtUtc.Value < now
            )
            {
                return false;
            }

            return !tenant.SubscriptionEndsAtUtc.HasValue || tenant.SubscriptionEndsAtUtc.Value >= now;
        }

        private static string HashPasswordLegacyCurrent(string password, string salt)
        {
            var bytes = Encoding.UTF8.GetBytes($"{salt}:{password}");
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }

        private static char PickRandomCharacter(string allowedCharacters)
        {
            return allowedCharacters[RandomNumberGenerator.GetInt32(allowedCharacters.Length)];
        }

        private static string HashPasswordLegacy(string password, string salt)
        {
            var saltBytes = Convert.FromBase64String(salt);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var payload = new byte[saltBytes.Length + passwordBytes.Length];
            Buffer.BlockCopy(saltBytes, 0, payload, 0, saltBytes.Length);
            Buffer.BlockCopy(passwordBytes, 0, payload, saltBytes.Length, passwordBytes.Length);
            var hash = SHA256.HashData(payload);
            return Convert.ToBase64String(hash);
        }
    }
}
