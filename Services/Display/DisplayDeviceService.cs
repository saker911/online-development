using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Users;

namespace VehiclePermitSystemWeb.Services.Display
{
    public sealed class DisplayDeviceService : IDisplayDeviceService
    {
        public string DeviceCookieName => "DisplayDeviceToken";
        public string RequestCookieName => "DisplayDeviceRequestCode";

        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;
        private readonly IPermitAuditService _auditService;
        private readonly IUserAdminService? _userAdminService;
        private readonly ConcurrentDictionary<string, DateTime> _rejectedAccessAuditThrottle = new();

        public DisplayDeviceService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock,
            IPermitAuditService auditService,
            IUserAdminService? userAdminService = null
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
            _auditService = auditService;
            _userAdminService = userAdminService;
        }

        public DisplaySecuritySettings GetSecuritySettings()
        {
            using var db = _dbContextFactory.CreateDbContext();
            return EnsureSecuritySettings(db);
        }

        public void UpdateSetupKey(string setupKey)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var settings = AdministrationSettingsService.GetAdministrationSettingsRecord(db, 1);
            if (settings == null)
            {
                settings = AdministrationSettingsService.BuildDefaultAdministrationSettings();
                db.AdministrationSettings.Add(settings);
            }

            settings.DisplayAccessKey =
                string.IsNullOrWhiteSpace(setupKey) || IsForbiddenSetupKey(setupKey)
                    ? DisplayAccessKeyHasher.Hash(DisplayAccessDefaults.CreateAccessKey())
                    : DisplayAccessKeyHasher.Hash(setupKey.Trim());
            AdministrationSettingsService.NormalizeAdministrationSettings(settings);
            db.SaveChanges();
            _userAdminService?.InvalidateAdministrationSettingsCache();
        }

        public void InvalidateDeviceTrust(string actor)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var devices = db.DisplayDevices.Where(item =>
                item.Status == DisplayDeviceStatuses.Approved
            );

            foreach (var device in devices)
            {
                device.DeviceTokenHash = string.Empty;
                device.RequestCode = SecureTokenGenerator.GenerateSecureToken();
                device.LastSeenUtc = null;
                device.LastIpAddress = string.Empty;
                RecordDeviceAudit(
                    db,
                    device,
                    "DisplayDeviceTrustInvalidated",
                    "إيقاف ربط شاشة العرض بعد تغيير المفتاح",
                    actor
                );
            }

            db.SaveChanges();
        }

        public DisplayDevice? GetApprovedDevice(HttpContext context)
        {
            var device = GetDeviceFromCookie(context);
            if (device == null)
            {
                return null;
            }

            if (device.Status != DisplayDeviceStatuses.Approved)
            {
                RecordRejectedAccess(context, device.Status);
                return null;
            }

            return device;
        }

        public DisplayDevice? GetDeviceFromCookie(HttpContext context)
        {
            var token = context.Request.Cookies[DeviceCookieName];
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var tokenHash = HashSecret(token);
            using var db = _dbContextFactory.CreateDbContext();
            var device = db.DisplayDevices.FirstOrDefault(item =>
                item.DeviceTokenHash == tokenHash
            );
            if (device == null)
            {
                RecordRejectedAccess(context, "unknown-token");
            }

            return device;
        }

        public DisplayDevice? GetDeviceFromRequestCookie(HttpContext context)
        {
            var requestCode = context.Request.Cookies[RequestCookieName];
            if (string.IsNullOrWhiteSpace(requestCode))
            {
                return null;
            }

            using var db = _dbContextFactory.CreateDbContext();
            return db
                .DisplayDevices.AsNoTracking()
                .FirstOrDefault(item => item.RequestCode == requestCode);
        }

        public DisplayDevice? GetDeviceById(int id)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return db.DisplayDevices.AsNoTracking().FirstOrDefault(item => item.Id == id);
        }

        public DisplayDevice? RegisterRequest(
            DisplayDeviceRegistrationViewModel model,
            HttpContext context
        )
        {
            var setupKey = (model.SetupKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(setupKey) || IsForbiddenSetupKey(setupKey))
            {
                return null;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var administrationSettings =
                AdministrationSettingsService.GetAdministrationSettingsRecord(db, 1)
                ?? AdministrationSettingsService.BuildDefaultAdministrationSettings();
            AdministrationSettingsService.NormalizeAdministrationSettings(administrationSettings);
            if (!DisplayAccessKeyHasher.Verify(administrationSettings.DisplayAccessKey, setupKey))
            {
                return null;
            }

            var existing = GetDeviceFromRequestCookie(context);
            if (existing != null)
            {
                var trackedExisting = db.DisplayDevices.FirstOrDefault(item =>
                    item.Id == existing.Id
                );
                if (trackedExisting != null)
                {
                    trackedExisting.ScreenName = (
                        model.ScreenName ?? trackedExisting.ScreenName
                    ).Trim();
                    trackedExisting.ScreenLocation = (
                        model.ScreenLocation ?? trackedExisting.ScreenLocation
                    ).Trim();
                    trackedExisting.Description = (
                        model.Description ?? trackedExisting.Description
                    ).Trim();
                    trackedExisting.LastIpAddress = ResolveIp(context);
                    trackedExisting.UserAgent = context.Request.Headers.UserAgent.ToString();
                    db.SaveChanges();
                    return trackedExisting;
                }
            }

            var now = _systemClock.UtcNow;
            var device = new DisplayDevice
            {
                ScreenName = (model.ScreenName ?? string.Empty).Trim(),
                ScreenLocation = (model.ScreenLocation ?? string.Empty).Trim(),
                Description = (model.Description ?? string.Empty).Trim(),
                Mode = DisplayDeviceModes.Gate,
                Status = DisplayDeviceStatuses.Pending,
                RequestCode = SecureTokenGenerator.GenerateSecureToken(12),
                IpAddress = ResolveIp(context),
                LastIpAddress = ResolveIp(context),
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                CreatedAtUtc = now,
            };
            db.DisplayDevices.Add(device);
            _auditService.RecordUserActivity(
                db,
                "display-device",
                device.ScreenName,
                "DisplayDeviceRequested",
                "طلب اعتماد شاشة عرض جديد",
                $"تم إرسال طلب اعتماد شاشة العرض {device.ScreenName} من {device.ScreenLocation}.",
                nameof(DisplayDeviceService),
                "display-device",
                now
            );
            db.SaveChanges();
            return device;
        }

        public (bool Success, string? DeviceToken) ApproveDevice(
            int id,
            string approvedBy,
            HttpContext? context = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var device = db.DisplayDevices.FirstOrDefault(item => item.Id == id);
            if (device == null)
            {
                return (false, null);
            }

            var token = SecureTokenGenerator.GenerateSecureToken();
            device.DeviceTokenHash = HashSecret(token);
            device.Status = DisplayDeviceStatuses.Approved;
            device.ApprovedAtUtc = _systemClock.UtcNow;
            device.ApprovedByUserId = approvedBy ?? string.Empty;
            device.DisabledAtUtc = null;
            RecordDeviceAudit(db, device, "DisplayDeviceApproved", "اعتماد شاشة عرض", approvedBy);
            db.SaveChanges();

            if (context != null)
            {
                SetDeviceCookie(context, token, EnsureSecuritySettings(db).DeviceCookieDays);
            }

            return (true, token);
        }

        public bool RejectDevice(int id, string actor) =>
            ChangeStatus(
                id,
                DisplayDeviceStatuses.Rejected,
                actor,
                "DisplayDeviceRejected",
                "رفض شاشة عرض"
            );

        public bool DisableDevice(int id, string actor)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var device = db.DisplayDevices.FirstOrDefault(item => item.Id == id);
            if (device == null)
            {
                return false;
            }

            device.Status = DisplayDeviceStatuses.Disabled;
            device.DisabledAtUtc = _systemClock.UtcNow;
            RecordDeviceAudit(db, device, "DisplayDeviceDisabled", "تعطيل شاشة عرض", actor);
            db.SaveChanges();
            return true;
        }

        public bool ActivateDevice(int id, string actor) =>
            ChangeStatus(
                id,
                DisplayDeviceStatuses.Approved,
                actor,
                "DisplayDeviceActivated",
                "تنشيط شاشة عرض"
            );

        public bool UpdateDeviceMode(int id, string mode, string actor)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var device = db.DisplayDevices.FirstOrDefault(item => item.Id == id);
            if (device == null)
            {
                return false;
            }

            var normalizedMode = DisplayDeviceModes.Normalize(mode);
            if (string.Equals(device.Mode, normalizedMode, StringComparison.Ordinal))
            {
                return true;
            }

            device.Mode = normalizedMode;
            RecordDeviceAudit(
                db,
                device,
                "DisplayDeviceModeChanged",
                $"تغيير وضع الشاشة إلى {DisplayDeviceModes.GetDisplayName(normalizedMode)}",
                actor
            );
            db.SaveChanges();
            return true;
        }

        public bool DeleteDevice(int id, string actor)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var device = db.DisplayDevices.FirstOrDefault(item => item.Id == id);
            if (device == null)
            {
                return false;
            }

            RecordDeviceAudit(db, device, "DisplayDeviceDeleted", "حذف شاشة عرض", actor);
            db.DisplayDevices.Remove(device);
            db.SaveChanges();
            return true;
        }

        public (bool Success, string? DeviceToken) RotateDeviceToken(
            int id,
            string actor,
            HttpContext? context = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var device = db.DisplayDevices.FirstOrDefault(item => item.Id == id);
            if (device == null)
            {
                return (false, null);
            }

            var token = SecureTokenGenerator.GenerateSecureToken();
            device.DeviceTokenHash = HashSecret(token);
            device.RequestCode = SecureTokenGenerator.GenerateSecureToken(12);
            RecordDeviceAudit(
                db,
                device,
                "DisplayDeviceTokenRotated",
                "تدوير رمز جهاز شاشة عرض",
                actor
            );
            db.SaveChanges();
            if (context != null)
            {
                SetDeviceCookie(context, token, EnsureSecuritySettings(db).DeviceCookieDays);
            }

            return (true, token);
        }

        public bool TryActivateApprovedRequest(HttpContext context)
        {
            var currentToken = context.Request.Cookies[DeviceCookieName];
            if (!string.IsNullOrWhiteSpace(currentToken))
            {
                var currentHash = HashSecret(currentToken);
                using var currentDb = _dbContextFactory.CreateDbContext();
                var currentDevice = currentDb.DisplayDevices.FirstOrDefault(item =>
                    item.DeviceTokenHash == currentHash
                    && item.Status == DisplayDeviceStatuses.Approved
                );
                if (currentDevice != null)
                {
                    currentDevice.LastSeenUtc = _systemClock.UtcNow;
                    currentDevice.LastIpAddress = ResolveIp(context);
                    currentDb.SaveChanges();
                    SetRequestCookie(context, currentDevice.RequestCode);
                    return true;
                }
            }

            var requestCode = context.Request.Cookies[RequestCookieName];
            if (string.IsNullOrWhiteSpace(requestCode))
            {
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var device = db.DisplayDevices.FirstOrDefault(item =>
                item.RequestCode == requestCode && item.Status == DisplayDeviceStatuses.Approved
            );
            if (device == null)
            {
                return false;
            }

            var token = SecureTokenGenerator.GenerateSecureToken();
            device.DeviceTokenHash = HashSecret(token);
            device.RequestCode = SecureTokenGenerator.GenerateSecureToken(12);
            device.LastIpAddress = ResolveIp(context);
            device.LastSeenUtc = _systemClock.UtcNow;
            RecordDeviceAudit(
                db,
                device,
                "DisplayDeviceTokenIssued",
                "إرسال رمز جهاز شاشة عرض",
                "display-device"
            );
            db.SaveChanges();
            SetDeviceCookie(context, token, EnsureSecuritySettings(db).DeviceCookieDays);
            SetRequestCookie(context, device.RequestCode);
            return true;
        }

        public bool RecordHeartbeat(HttpContext context)
        {
            TryActivateApprovedRequest(context);
            var device = GetApprovedDevice(context);
            if (device == null)
            {
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var tracked = db.DisplayDevices.FirstOrDefault(item => item.Id == device.Id);
            if (tracked == null || tracked.Status != DisplayDeviceStatuses.Approved)
            {
                return false;
            }

            tracked.LastSeenUtc = _systemClock.UtcNow;
            tracked.LastIpAddress = ResolveIp(context);
            db.SaveChanges();
            return true;
        }

        public DisplayDeviceManagementViewModel BuildManagementViewModel()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var now = _systemClock.UtcNow;
            var items = db
                .DisplayDevices.AsNoTracking()
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToList()
                .Select(item => ToListItem(item, now))
                .ToList();

            return new DisplayDeviceManagementViewModel
            {
                Devices = items,
                ApprovedCount = items.Count(item => item.Status == DisplayDeviceStatuses.Approved),
                OnlineCount = items.Count(item =>
                    item.Status == DisplayDeviceStatuses.Approved && item.IsOnline
                ),
                OfflineCount = items.Count(item =>
                    item.Status == DisplayDeviceStatuses.Approved && !item.IsOnline
                ),
                PendingCount = items.Count(item => item.Status == DisplayDeviceStatuses.Pending),
            };
        }

        public int GetPendingCount()
        {
            using var db = _dbContextFactory.CreateDbContext();
            return db.DisplayDevices.Count(item => item.Status == DisplayDeviceStatuses.Pending);
        }

        public void RecordRejectedAccess(HttpContext context, string reason)
        {
            var now = _systemClock.UtcNow;
            var throttleKey = $"{ResolveIp(context)}:{reason}";
            if (
                _rejectedAccessAuditThrottle.TryGetValue(throttleKey, out var lastRecordedAt)
                && now - lastRecordedAt < TimeSpan.FromMinutes(5)
            )
            {
                return;
            }

            _rejectedAccessAuditThrottle[throttleKey] = now;
            if (_rejectedAccessAuditThrottle.Count > 2048)
            {
                foreach (
                    var stale in _rejectedAccessAuditThrottle.Where(item =>
                        now - item.Value > TimeSpan.FromHours(1)
                    )
                )
                {
                    _rejectedAccessAuditThrottle.TryRemove(stale.Key, out _);
                }
            }

            using var db = _dbContextFactory.CreateDbContext();
            _auditService.RecordUserActivity(
                db,
                "display-device",
                "شاشة عرض غير معتمدة",
                "DisplayDeviceAccessRejected",
                "محاولة دخول من جهاز غير معتمد",
                $"تم رفض وصول شاشة عرض. السبب: {reason}. IP: {ResolveIp(context)}",
                nameof(DisplayDeviceService),
                "display-device",
                now
            );
            db.SaveChanges();
        }

        private bool ChangeStatus(
            int id,
            string status,
            string actor,
            string actionType,
            string actionLabel
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var device = db.DisplayDevices.FirstOrDefault(item => item.Id == id);
            if (device == null)
            {
                return false;
            }

            device.Status = status;
            if (status == DisplayDeviceStatuses.Approved)
            {
                device.DisabledAtUtc = null;
            }

            RecordDeviceAudit(db, device, actionType, actionLabel, actor);
            db.SaveChanges();
            return true;
        }

        private DisplaySecuritySettings EnsureSecuritySettings(ApplicationDbContext db)
        {
            var settings = db.DisplaySecuritySettings.FirstOrDefault(item => item.Id == 1);
            if (settings != null)
            {
                if (settings.HeartbeatSeconds <= 0)
                {
                    settings.HeartbeatSeconds = 30;
                }
                if (settings.DeviceCookieDays <= 0 || settings.DeviceCookieDays > 30)
                {
                    settings.DeviceCookieDays = 30;
                }
                if (string.IsNullOrWhiteSpace(settings.SetupKeyHash))
                {
                    settings.SetupKeyHash = HashSecret(SecureTokenGenerator.GenerateSecureToken());
                }
                db.SaveChanges();
                return settings;
            }

            settings = new DisplaySecuritySettings
            {
                Id = 1,
                SetupKeyHash = HashSecret(SecureTokenGenerator.GenerateSecureToken()),
                RequireAdminApproval = true,
                HeartbeatSeconds = 30,
                DeviceCookieDays = 30,
            };
            db.DisplaySecuritySettings.Add(settings);
            db.SaveChanges();
            return settings;
        }

        private static DisplayDeviceListItemViewModel ToListItem(DisplayDevice device, DateTime now)
        {
            var isOnline =
                device.LastSeenUtc.HasValue
                && now - device.LastSeenUtc.Value <= TimeSpan.FromMinutes(2);
            return new DisplayDeviceListItemViewModel
            {
                Id = device.Id,
                ScreenName = device.ScreenName,
                ScreenLocation = device.ScreenLocation,
                Description = device.Description,
                Mode = DisplayDeviceModes.Normalize(device.Mode),
                ModeText = DisplayDeviceModes.GetDisplayName(device.Mode),
                Status = device.Status,
                StatusText = device.Status switch
                {
                    DisplayDeviceStatuses.Pending => "بانتظار الموافقة",
                    DisplayDeviceStatuses.Disabled => "معطلة",
                    DisplayDeviceStatuses.Rejected => "مرفوضة",
                    DisplayDeviceStatuses.Approved when isOnline => "تعمل الآن",
                    DisplayDeviceStatuses.Approved => "غير متصلة",
                    _ => device.Status,
                },
                IsOnline = isOnline,
                IpAddress = device.IpAddress,
                LastIpAddress = device.LastIpAddress,
                UserAgentSummary =
                    device.UserAgent.Length > 80
                        ? device.UserAgent[..80] + "..."
                        : device.UserAgent,
                RequestCode = device.RequestCode,
                CreatedAtUtc = device.CreatedAtUtc,
                LastSeenUtc = device.LastSeenUtc,
                ApprovedAtUtc = device.ApprovedAtUtc,
                ApprovedByUserId = device.ApprovedByUserId,
                Notes = device.Notes,
            };
        }

        private void RecordDeviceAudit(
            ApplicationDbContext db,
            DisplayDevice device,
            string actionType,
            string actionLabel,
            string? actor
        )
        {
            _auditService.RecordUserActivity(
                db,
                $"display-device:{device.Id}",
                device.ScreenName,
                actionType,
                actionLabel,
                $"{actionLabel}: {device.ScreenName} - {device.ScreenLocation}.",
                nameof(DisplayDeviceService),
                actor,
                _systemClock.UtcNow
            );
        }

        public void SetRequestCookie(HttpContext context, string requestCode)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SetCookie(
                context,
                RequestCookieName,
                requestCode,
                EnsureSecuritySettings(db).DeviceCookieDays
            );
        }

        private static void SetDeviceCookie(HttpContext context, string token, int days)
        {
            SetCookie(context, "DisplayDeviceToken", token, days);
        }

        private static void SetCookie(HttpContext context, string name, string value, int days)
        {
            var environment = context.RequestServices?.GetService<IHostEnvironment>();
            context.Response.Cookies.Append(
                name,
                value,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure =
                        context.Request.IsHttps
                        || environment?.IsDevelopment() == false,
                    Expires = DateTimeOffset.UtcNow.AddDays(days),
                    IsEssential = true,
                }
            );
        }

        private static string ResolveIp(HttpContext context) =>
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        private static bool IsForbiddenSetupKey(string key) =>
            string.Equals(key.Trim(), "display-2026-secure", StringComparison.Ordinal);

        private static string HashSecret(string secret)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
            return Convert.ToBase64String(hash);
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
