using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Users;

namespace VehiclePermitSystemWeb.Services.Users
{
    public sealed class UserAdminService : IUserAdminService
    {
        private static readonly TimeSpan AdministrationSettingsCacheLifetime =
            TimeSpan.FromMinutes(1);
        private const string AdministrationSettingsCachePrefix = "administration-settings:";
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;
        private readonly ISystemClock _systemClock;
        private readonly IPermitAuditService _permitAuditService;
        private readonly UserSessionService _userSessionService;
        private readonly TimeSpan _displayOperatorSessionLifetime;
        private readonly ConcurrentDictionary<
            string,
            DisplayOperatorSessionInfo
        > _displayOperatorSessions = new(StringComparer.OrdinalIgnoreCase);

        public UserAdminService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IConfiguration configuration,
            IMemoryCache memoryCache,
            ISystemClock systemClock,
            IPermitAuditService permitAuditService,
            UserSessionService userSessionService
        )
        {
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
            _memoryCache = memoryCache;
            _systemClock = systemClock;
            _permitAuditService = permitAuditService;
            _userSessionService = userSessionService;
            _displayOperatorSessionLifetime = TimeSpan.FromMinutes(
                Math.Clamp(
                    _configuration.GetValue("Security:DisplayOperatorSessionMinutes", 30),
                    5,
                    720
                )
            );
        }

        public AdministrationSettings GetAdministrationSettings()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var cacheKey = BuildAdministrationSettingsCacheKey(db.CurrentTenantId);
            if (
                _memoryCache.TryGetValue(cacheKey, out AdministrationSettings? cachedSettings)
                && cachedSettings != null
            )
            {
                return AdministrationSettingsService.CloneAdministrationSettings(cachedSettings)
                    ?? AdministrationSettingsService.BuildDefaultAdministrationSettings();
            }

            var tenantExists = db.Tenants.AsNoTracking().Any(tenant =>
                tenant.TenantId == db.CurrentTenantId && tenant.IsActive
            );
            var record = tenantExists
                ? AdministrationSettingsService.GetAdministrationSettingsRecord(db, 1)
                : db
                    .AdministrationSettings.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(settings => settings.TenantId == TenantDefaults.DefaultTenantId)
                    .OrderByDescending(settings => settings.Id == 1)
                    .ThenBy(settings => settings.Id)
                    .FirstOrDefault();
            if (record == null)
            {
                record = AdministrationSettingsService.BuildDefaultAdministrationSettings();
                db.AdministrationSettings.Add(record);
                db.SaveChanges();
            }

            var settings =
                AdministrationSettingsService.CloneAdministrationSettings(record)
                ?? AdministrationSettingsService.BuildDefaultAdministrationSettings();
            AdministrationSettingsService.NormalizeAdministrationSettings(settings);
            AdministrationSettingsService.ApplyAdministrationGeneralManagerLink(db, settings);
            var cacheSnapshot =
                AdministrationSettingsService.CloneAdministrationSettings(settings)
                ?? AdministrationSettingsService.BuildDefaultAdministrationSettings();
            _memoryCache.Set(
                cacheKey,
                cacheSnapshot,
                AdministrationSettingsCacheLifetime
            );
            return AdministrationSettingsService.CloneAdministrationSettings(cacheSnapshot)
                ?? AdministrationSettingsService.BuildDefaultAdministrationSettings();
        }

        public AdministrationSettings GetAdministrationSettings(
            string tenantId,
            bool ignoreTenantFilters = false
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedTenantId = string.IsNullOrWhiteSpace(tenantId)
                ? db.CurrentTenantId
                : tenantId.Trim();
            if (
                !ignoreTenantFilters
                && !string.Equals(
                    normalizedTenantId,
                    db.CurrentTenantId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                normalizedTenantId = db.CurrentTenantId;
            }

            var cacheKey = BuildAdministrationSettingsCacheKey(normalizedTenantId);
            if (
                _memoryCache.TryGetValue(cacheKey, out AdministrationSettings? cachedSettings)
                && cachedSettings != null
            )
            {
                return AdministrationSettingsService.CloneAdministrationSettings(cachedSettings);
            }

            var record = db
                .AdministrationSettings.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(settings => settings.TenantId == normalizedTenantId)
                .OrderByDescending(settings => settings.Id == 1)
                .ThenBy(settings => settings.Id)
                .FirstOrDefault();
            if (record == null)
            {
                return new AdministrationSettings { TenantId = normalizedTenantId };
            }

            var settings = AdministrationSettingsService.CloneAdministrationSettings(record);
            AdministrationSettingsService.NormalizeAdministrationSettings(settings);

            var linkedUsername = (settings.GeneralManagerUsername ?? string.Empty).Trim();
            var linkedManager = string.IsNullOrWhiteSpace(linkedUsername)
                ? null
                : db
                    .UserAccounts.IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefault(user =>
                        user.TenantId == normalizedTenantId && user.Username == linkedUsername
                    );
            if (AdministrationSettingsService.IsEligibleGeneralManager(linkedManager))
            {
                settings.ManagerName = linkedManager!.DisplayName;
                settings.ManagerTitle = linkedManager.JobTitle;
                settings.ManagerPhoneNumber = linkedManager.PhoneNumber;
            }

            var cacheSnapshot = AdministrationSettingsService.CloneAdministrationSettings(settings);
            _memoryCache.Set(cacheKey, cacheSnapshot, AdministrationSettingsCacheLifetime);
            return AdministrationSettingsService.CloneAdministrationSettings(cacheSnapshot);
        }

        public void UpdateAdministrationSettings(AdministrationSettings settings)
        {
            using var db = _dbContextFactory.CreateDbContext();
            AdministrationSettingsService.NormalizeAdministrationSettings(settings);
            AdministrationSettingsService.ApplyAdministrationGeneralManagerLink(db, settings);
            var existing = AdministrationSettingsService.GetAdministrationSettingsRecord(
                db,
                settings.Id
            );
            if (existing == null)
            {
                settings.Id = 1;
                db.AdministrationSettings.Add(settings);
            }
            else
            {
                existing.IsInitialSetupCompleted =
                    existing.IsInitialSetupCompleted || settings.IsInitialSetupCompleted;
                existing.OrganizationName = settings.OrganizationName;
                existing.DepartmentName = settings.DepartmentName;
                existing.Address = settings.Address;
                existing.Phone = settings.Phone;
                existing.Email = settings.Email;
                existing.SignatureText = settings.SignatureText;
                existing.LogoPath = settings.LogoPath;
                existing.SignatureImagePath = settings.SignatureImagePath;
                existing.SignatureImageData = settings.SignatureImageData;
                existing.SignatureImageContentType = settings.SignatureImageContentType;
                existing.DisplayBaseUrl = settings.DisplayBaseUrl;
                existing.DisplayAccessKey = settings.DisplayAccessKey;
                existing.AllowedClientIpRanges = settings.AllowedClientIpRanges;
                existing.WorkStartTime = settings.WorkStartTime;
                existing.WorkEndTime = settings.WorkEndTime;
                existing.AttendanceGraceMinutes = settings.AttendanceGraceMinutes;
                existing.WorkEndExitGraceMinutes = settings.WorkEndExitGraceMinutes;
                existing.LateReturnGraceMinutes = settings.LateReturnGraceMinutes;
                existing.LeaveRequestsEnabled = settings.LeaveRequestsEnabled;
                existing.OfficialWorkDaysCsv = settings.OfficialWorkDaysCsv;
                AdministrationSettingsService.NormalizeAdministrationSettings(existing);
                AdministrationSettingsService.ApplyAdministrationGeneralManagerLink(db, existing);
            }

            db.SaveChanges();
            _memoryCache.Remove(BuildAdministrationSettingsCacheKey(db.CurrentTenantId));
        }

        public bool ValidateDisplayAccessKey(string? accessKey)
        {
            var normalizedAccessKey = (accessKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedAccessKey))
            {
                return false;
            }

            var settings = GetAdministrationSettings();
            return DisplayAccessKeyHasher.Verify(settings.DisplayAccessKey, normalizedAccessKey);
        }

        public IEnumerable<UserAccount> GetAllUsers(bool ignoreTenantFilters = false)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return UserReadMapper.GetAllUsers(db, ignoreTenantFilters);
        }

        public void InvalidateAdministrationSettingsCache()
        {
            using var db = _dbContextFactory.CreateDbContext();
            _memoryCache.Remove(BuildAdministrationSettingsCacheKey(db.CurrentTenantId));
        }

        public IEnumerable<Tenant> GetTenants(bool includeInactive = false)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var tenants = db.Tenants.AsNoTracking().AsQueryable();
            if (!includeInactive)
            {
                tenants = tenants.Where(tenant => tenant.IsActive);
            }

            return tenants.OrderBy(tenant => tenant.Name).ToList();
        }

        public IEnumerable<UserActivity> GetRecentUserActivities(int take = 20)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return db
                .UserActivities.AsNoTracking()
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id)
                .Take(take)
                .ToList();
        }

        public IEnumerable<UserActivity> GetUserActivities(
            string? query = null,
            string? actionType = null,
            string? username = null,
            int take = 200
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedQuery = (query ?? string.Empty).Trim();
            var normalizedActionType = (actionType ?? string.Empty).Trim();
            var normalizedUsername = (username ?? string.Empty).Trim();

            var activities = db.UserActivities.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(normalizedActionType))
            {
                activities = activities.Where(x => x.ActionType == normalizedActionType);
            }

            if (!string.IsNullOrWhiteSpace(normalizedUsername))
            {
                activities = activities.Where(x => x.Username == normalizedUsername);
            }

            var filtered = activities
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id)
                .Take(take)
                .ToList();

            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return filtered;
            }

            return filtered
                .Where(x =>
                    TextSearchMatcher.ContainsValue(x.Username, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(x.DisplayName, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(x.ActionLabel, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(x.ActionType, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(x.Message, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(x.Source, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(x.RecordedBy, normalizedQuery)
                )
                .ToList();
        }

        public UserAccount? GetUserAccount(string username, bool ignoreTenantFilters = false)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return UserReadMapper.GetUserAccount(db, username, ignoreTenantFilters);
        }

        public bool HasAnyUsers()
        {
            using var db = _dbContextFactory.CreateDbContext();
            return UserReadMapper.HasAnyUsers(db);
        }

        public bool IsInitialSetupRequired()
        {
            using var db = _dbContextFactory.CreateDbContext();
            if (!db.UserAccounts.IgnoreQueryFilters().AsNoTracking().Any())
            {
                return true;
            }

            var settings = db
                .AdministrationSettings.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == TenantDefaults.DefaultTenantId)
                .OrderByDescending(item => item.Id == 1)
                .ThenBy(item => item.Id)
                .FirstOrDefault();
            return settings?.IsInitialSetupCompleted != true;
        }

        public bool CompleteInitialSetup(InitialSetupViewModel model)
        {
            using var db = _dbContextFactory.CreateDbContext();
            if (db.UserAccounts.Any())
            {
                return false;
            }

            var normalizedUsername = (model.Username ?? string.Empty).Trim();
            var normalizedFullName = (model.FullName ?? string.Empty).Trim();
            var normalizedPhoneNumber = (model.PhoneNumber ?? string.Empty).Trim();
            var normalizedJobTitle = (model.JobTitle ?? string.Empty).Trim();
            var normalizedOrganizationName = (model.OrganizationName ?? string.Empty).Trim();
            var normalizedAdministrationPhone = (model.AdministrationPhone ?? string.Empty).Trim();
            var normalizedAdministrationEmail = (model.AdministrationEmail ?? string.Empty).Trim();
            var normalizedAdministrationAddress = (
                model.AdministrationAddress ?? string.Empty
            ).Trim();
            var password = model.Password ?? string.Empty;

            if (
                string.IsNullOrWhiteSpace(normalizedUsername)
                || string.IsNullOrWhiteSpace(normalizedFullName)
                || !SaudiMobileNumberValidator.IsValidRequired(normalizedPhoneNumber)
                || string.IsNullOrWhiteSpace(normalizedJobTitle)
                || string.IsNullOrWhiteSpace(normalizedOrganizationName)
                || !SaudiLandlineNumberValidator.IsValidRequired(normalizedAdministrationPhone)
                || string.IsNullOrWhiteSpace(normalizedAdministrationEmail)
                || string.IsNullOrWhiteSpace(normalizedAdministrationAddress)
                || !PasswordValidationRules.IsStrongPassword(password)
            )
            {
                return false;
            }

            var user = UserAccountService.BuildInitialSystemOwner(
                normalizedUsername,
                normalizedFullName,
                normalizedPhoneNumber,
                normalizedJobTitle,
                mustChangePassword: false
            );
            UserAccountService.SetPassword(user, password);
            db.UserAccounts.Add(user);

            var settings = AdministrationSettingsService.GetAdministrationSettingsRecord(db, 1);
            if (settings == null)
            {
                settings = AdministrationSettingsService.BuildDefaultAdministrationSettings();
                db.AdministrationSettings.Add(settings);
            }

            settings.OrganizationName = normalizedOrganizationName;
            settings.DepartmentName = string.IsNullOrWhiteSpace(settings.DepartmentName)
                ? "الإدارة العامة"
                : settings.DepartmentName;
            settings.Phone = normalizedAdministrationPhone;
            settings.Email = normalizedAdministrationEmail;
            settings.Address = normalizedAdministrationAddress;
            settings.LogoPath = string.IsNullOrWhiteSpace(model.LogoPath)
                ? null
                : model.LogoPath.Trim();
            settings.IsInitialSetupCompleted = true;
            AdministrationSettingsService.NormalizeAdministrationSettings(settings);
            AdministrationSettingsService.EnsureDepartmentExists(db, settings.DepartmentName);

            db.SaveChanges();
            _memoryCache.Remove(BuildAdministrationSettingsCacheKey(db.CurrentTenantId));
            return true;
        }

        private static string BuildAdministrationSettingsCacheKey(string? tenantId)
        {
            return AdministrationSettingsCachePrefix
                + (string.IsNullOrWhiteSpace(tenantId)
                    ? TenantDefaults.DefaultTenantId
                    : tenantId.Trim().ToLowerInvariant());
        }

        private static int GetNextAdministrationSettingsId(ApplicationDbContext db)
        {
            return db
                    .AdministrationSettings.IgnoreQueryFilters()
                    .Select(settings => (int?)settings.Id)
                    .Max()
                + 1
                ?? 1;
        }

        public bool CreateUser(UserAccount user, string password)
        {
            using var db = _dbContextFactory.CreateDbContext();
            if (db.UserAccounts.IgnoreQueryFilters().Any(u => u.Username == user.Username))
            {
                return false;
            }

            var normalizedTenantId = ResolveActiveTenantId(db, user.TenantId);
            if (string.IsNullOrWhiteSpace(normalizedTenantId))
            {
                return false;
            }

            user.TenantId = normalizedTenantId;
            user.IsSuperAdmin = false;
            UserPermissionService.NormalizePrivilegedAssignments(user);
            user.OperatorBadgeCode = UserAccountService.NormalizeOperatorBadgeCode(
                user.OperatorBadgeCode
            );
            UserAccountService.SetPassword(user, password);
            user.MustChangePassword = true;
            db.UserAccounts.Add(user);
            SyncUserAssignmentState(
                db,
                user,
                forceGeneralManagerLink: user.IsActive
                    && string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
            );
            db.SaveChanges();
            return true;
        }

        public bool UpdateUser(
            UserAccount user,
            string? newPassword = null,
            string? originalUsername = null,
            bool ignoreTenantFilters = false
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedOriginalUsername = string.IsNullOrWhiteSpace(originalUsername)
                ? (user.Username ?? string.Empty).Trim()
                : originalUsername.Trim();
            var userAccounts = ignoreTenantFilters
                ? db.UserAccounts.IgnoreQueryFilters()
                : db.UserAccounts;
            var existing = userAccounts.FirstOrDefault(u => u.Username == normalizedOriginalUsername);
            if (existing == null)
            {
                return false;
            }

            var requestedUsername = string.IsNullOrWhiteSpace(user.Username)
                ? normalizedOriginalUsername
                : user.Username.Trim();
            var isRenaming = !string.Equals(
                requestedUsername,
                normalizedOriginalUsername,
                StringComparison.OrdinalIgnoreCase
            );

            var wasGeneralManager = string.Equals(
                existing.Role,
                AppRoles.GeneralManager,
                StringComparison.OrdinalIgnoreCase
            );
            var wasActive = existing.IsActive;
            var wasLinkedGeneralManager =
                AdministrationSettingsService.IsAdministrationGeneralManager(db, existing.Username);
            var isProtectedSuperAdmin = existing.IsSuperAdmin;
            user.IsSuperAdmin = existing.IsSuperAdmin;
            user.Username = requestedUsername;
            UserPermissionService.NormalizePrivilegedAssignments(user);

            if (isProtectedSuperAdmin)
            {
                user.IsActive = true;
            }

            if (isRenaming)
            {
                if (!isProtectedSuperAdmin)
                {
                    return false;
                }

                var usernameAlreadyUsed = db
                    .UserAccounts.IgnoreQueryFilters()
                    .Any(account =>
                        account.Username != normalizedOriginalUsername
                        && account.Username == requestedUsername
                    );
                if (usernameAlreadyUsed)
                {
                    return false;
                }
            }

            var departmentAccounts = ignoreTenantFilters
                ? db.Departments.IgnoreQueryFilters()
                : db.Departments;
            var managedDepartment = departmentAccounts.FirstOrDefault(department =>
                department.ManagerUsername == existing.Username
            );
            if (
                managedDepartment != null
                && !(
                    user.IsActive
                    && string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        user.Department,
                        managedDepartment.Name,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                return false;
            }

            var normalizedTenantId = isProtectedSuperAdmin
                ? existing.TenantId
                : ResolveActiveTenantId(db, user.TenantId);
            if (string.IsNullOrWhiteSpace(normalizedTenantId))
            {
                return false;
            }

            existing.TenantId = normalizedTenantId;
            existing.DisplayName = user.DisplayName;
            existing.FullName = user.FullName;
            existing.Department = user.Department;
            existing.JobTitle = user.JobTitle;
            existing.ManagerUsername = user.ManagerUsername;
            existing.OperatorBadgeCode = UserAccountService.NormalizeOperatorBadgeCode(
                user.OperatorBadgeCode
            );
            existing.PhoneNumber = user.PhoneNumber;
            existing.Email = user.Email;
            existing.Role = user.Role;
            existing.IsActive = user.IsActive;
            existing.IsSuperAdmin = isProtectedSuperAdmin;
            existing.CanViewDashboard = user.CanViewDashboard;
            existing.CanViewPermits = user.CanViewPermits;
            existing.CanViewVisitorPermits = user.CanViewVisitorPermits;
            existing.CanCreatePermit = user.CanCreatePermit;
            existing.CanCreateVisitorPermit = user.CanCreateVisitorPermit;
            existing.CanEditPermit = user.CanEditPermit;
            existing.CanEditVisitorPermit = user.CanEditVisitorPermit;
            existing.CanApprovePermit = user.CanApprovePermit;
            existing.CanApproveLeaveRequest = user.CanApproveLeaveRequest;
            existing.CanStopPermit = user.CanStopPermit;
            existing.CanReviewUnauthorizedExit = user.CanReviewUnauthorizedExit;
            existing.CanViewVisits = user.CanViewVisits;
            existing.CanCreateVisit = user.CanCreateVisit;
            existing.CanEditVisit = user.CanEditVisit;
            existing.CanApproveDetainedVisit = user.CanApproveDetainedVisit;
            existing.CanApproveVisits = user.CanApproveVisits;
            existing.CanScanOperations = user.CanScanOperations;
            existing.CanViewDisplays = user.CanViewDisplays;
            existing.CanManageUsers = user.CanManageUsers;
            existing.CanManageDepartments = user.CanManageDepartments;
            existing.CanManageAdministration = user.CanManageAdministration;
            existing.CanManageDelegations = user.CanManageDelegations;
            existing.MustChangeOperatorPin = user.MustChangeOperatorPin;

            if (existing.IsSuperAdmin)
            {
                UserPermissionService.NormalizePrivilegedAssignments(existing);
                AppPermissions.ApplyRoleDefaults(existing);
            }

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                if (!PasswordValidationRules.IsStrongPassword(newPassword))
                {
                    return false;
                }

                UserAccountService.SetPassword(existing, newPassword);
                existing.MustChangePassword = true;
            }

            var promotedToGeneralManager =
                !wasGeneralManager
                && string.Equals(
                    existing.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                );
            var reactivatedGeneralManager =
                !wasActive
                && existing.IsActive
                && string.Equals(
                    existing.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                );

            UserAccount persistedUser = existing;
            if (isRenaming)
            {
                persistedUser =
                    VehiclePermitSystemWeb.Utilities.Users.UserCloneMapper.CloneUserAccount(
                        existing
                    );
                persistedUser.Username = requestedUsername;

                UserMigrationMapper.MigrateUsernameReferences(
                    db,
                    normalizedOriginalUsername,
                    requestedUsername,
                    persistedUser.DisplayName
                );
                UserMigrationMapper.SyncDisplayOperatorSessionIdentity(
                    _displayOperatorSessions,
                    normalizedOriginalUsername,
                    requestedUsername,
                    persistedUser.DisplayName
                );

                db.UserAccounts.Add(persistedUser);
                db.UserAccounts.Remove(existing);
            }

            SyncUserAssignmentState(
                db,
                persistedUser,
                forceGeneralManagerLink: promotedToGeneralManager || reactivatedGeneralManager,
                isLinkedGeneralManager: wasLinkedGeneralManager
            );

            db.SaveChanges();
            return true;
        }

        private static void SyncUserAssignmentState(
            ApplicationDbContext db,
            UserAccount user,
            bool forceGeneralManagerLink = false,
            bool isLinkedGeneralManager = false
        )
        {
            DepartmentManagerWorkflowService.SynchronizeDepartmentManagerState(
                db,
                user,
                user.Department,
                string.Equals(
                    user.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            AdministrationSettingsService.SyncAdministrationGeneralManagerForUser(
                db,
                user,
                forceLink: forceGeneralManagerLink,
                isLinkedGeneralManager: isLinkedGeneralManager
            );
        }

        private static string ResolveActiveTenantId(ApplicationDbContext db, string? tenantId)
        {
            var normalizedTenantId = string.IsNullOrWhiteSpace(tenantId)
                ? db.CurrentTenantId
                : tenantId.Trim();
            normalizedTenantId = string.IsNullOrWhiteSpace(normalizedTenantId)
                ? TenantDefaults.DefaultTenantId
                : normalizedTenantId;

            return db.Tenants.AsNoTracking().Any(tenant =>
                tenant.TenantId == normalizedTenantId && tenant.IsActive
            )
                ? normalizedTenantId
                : string.Empty;
        }

        public string? SetUserActiveStatus(
            string username,
            bool isActive,
            bool ignoreTenantFilters = false
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var userAccounts = ignoreTenantFilters
                ? db.UserAccounts.IgnoreQueryFilters()
                : db.UserAccounts;
            var existing = userAccounts.FirstOrDefault(u => u.Username == username);
            if (existing == null)
            {
                return null;
            }
            if (!isActive && existing.IsSuperAdmin)
            {
                return null;
            }
            // Prevent deactivating a department manager
            var departments = ignoreTenantFilters
                ? db.Departments.IgnoreQueryFilters()
                : db.Departments;
            var isDepartmentManager = departments.Any(d => d.ManagerUsername == existing.Username);
            if (!isActive && isDepartmentManager)
            {
                return null;
            }
            existing.IsActive = isActive;
            string? generatedTempPassword = string.Empty;
            if (isActive)
            {
                generatedTempPassword = UserAccountService.GenerateTemporaryPassword();
                UserAccountService.SetPassword(existing, generatedTempPassword);
                existing.MustChangePassword = true;

                existing.OperatorPinHash = string.Empty;
                existing.OperatorPinSalt = string.Empty;
                existing.MustChangeOperatorPin = false;
            }

            db.SaveChanges();
            return generatedTempPassword;
        }

        public bool ChangePassword(string username, string currentPassword, string newPassword)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return UserCredentialsMapper.ChangePassword(db, username, currentPassword, newPassword);
        }

        public string EnsureOperatorBadgeCode(
            string username,
            string? preferredBadgeCode = null,
            bool ignoreTenantFilters = false
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            return UserCredentialsMapper.EnsureOperatorBadgeCode(
                db,
                username,
                preferredBadgeCode,
                ignoreTenantFilters
            );
        }

        public string? ConfigureOperatorCredentials(
            string username,
            string badgeCode,
            string? temporaryPin,
            bool requirePinChange,
            bool ignoreTenantFilters = false
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            return UserCredentialsMapper.ConfigureOperatorCredentials(
                db,
                username,
                badgeCode,
                temporaryPin,
                requirePinChange,
                ignoreTenantFilters
            );
        }

        public bool IsUserDepartmentManager(string username)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return db.Departments.Any(d => d.ManagerUsername == username);
        }

        public DisplayOperatorSessionInfo? GetDisplayOperatorSession(string deviceId)
        {
            var normalizedDeviceId = UserAccountService.NormalizeDeviceId(deviceId);
            if (!_displayOperatorSessions.TryGetValue(normalizedDeviceId, out var session))
            {
                return null;
            }

            if (session.ExpiresAtUtc <= _systemClock.UtcNow)
            {
                _displayOperatorSessions.TryRemove(normalizedDeviceId, out _);
                return null;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var operatorStillAllowed = db.UserAccounts.AsNoTracking().Any(user =>
                user.Username == session.Username && user.IsActive && user.CanScanOperations
            );
            if (!operatorStillAllowed)
            {
                _displayOperatorSessions.TryRemove(normalizedDeviceId, out _);
                return null;
            }

            return UserCloneMapper.CloneDisplayOperatorSession(session);
        }

        public DisplayOperatorSwitchResult SwitchDisplayOperator(
            string deviceId,
            string badgeCode,
            string pin,
            string? recordedBy = null
        )
        {
            var normalizedDeviceId = UserAccountService.NormalizeDeviceId(deviceId);
            var normalizedBadge = UserAccountService.NormalizeOperatorBadgeCode(badgeCode);
            var normalizedPin = (pin ?? string.Empty).Trim();
            if (
                string.IsNullOrWhiteSpace(normalizedBadge)
                || !UserAccountService.IsOperatorPinValid(normalizedPin)
            )
            {
                return new DisplayOperatorSwitchResult
                {
                    Success = false,
                    ErrorCode = "invalid_operator_credentials",
                    Message = "بيانات دخول المشغل غير مكتملة.",
                };
            }

            using var db = _dbContextFactory.CreateDbContext();
            var user = db.UserAccounts.FirstOrDefault(u => u.OperatorBadgeCode == normalizedBadge);
            if (user == null || !user.IsActive || !user.CanScanOperations)
            {
                return new DisplayOperatorSwitchResult
                {
                    Success = false,
                    ErrorCode = "operator_not_allowed",
                    Message = "هذا المستخدم غير مخول بالعمل على شاشة البوابة.",
                };
            }

            if (!UserAccountService.VerifyOperatorPin(user, normalizedPin))
            {
                return new DisplayOperatorSwitchResult
                {
                    Success = false,
                    ErrorCode = "invalid_operator_pin",
                    Message = "الرمز السري غير صحيح.",
                };
            }

            var previousOperator = GetDisplayOperatorSession(normalizedDeviceId);
            var currentOperator = new DisplayOperatorSessionInfo
            {
                Username = user.Username,
                DisplayName = user.DisplayName,
                BadgeCode = user.OperatorBadgeCode,
                DeviceId = normalizedDeviceId,
                SignedInAtUtc = _systemClock.UtcNow,
                ExpiresAtUtc = _systemClock.UtcNow.Add(_displayOperatorSessionLifetime),
                MustChangePin = user.MustChangeOperatorPin,
            };

            _displayOperatorSessions[normalizedDeviceId] =
                UserCloneMapper.CloneDisplayOperatorSession(currentOperator)!;
            var occurredAt = _systemClock.UtcNow;
            _permitAuditService.RecordUserActivity(
                db,
                user.Username,
                user.DisplayName,
                "DisplayOperatorSignIn",
                "دخول مشغل بوابة",
                previousOperator == null
                    ? $"تم تسجيل دخول المشغل على شاشة البوابة ({normalizedDeviceId})."
                    : $"تم تبديل المشغل من {previousOperator.DisplayName} إلى {user.DisplayName} على شاشة البوابة ({normalizedDeviceId}).",
                "DisplayController",
                recordedBy ?? user.Username,
                occurredAt
            );

            if (
                previousOperator != null
                && !string.Equals(
                    previousOperator.Username,
                    user.Username,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                _permitAuditService.RecordUserActivity(
                    db,
                    previousOperator.Username,
                    previousOperator.DisplayName,
                    "DisplayOperatorSignOut",
                    "خروج مشغل بوابة",
                    $"تم إنهاء جلسة المشغل بسبب دخول {user.DisplayName} على شاشة البوابة ({normalizedDeviceId}).",
                    "DisplayController",
                    recordedBy ?? user.Username,
                    occurredAt
                );
            }

            db.SaveChanges();
            return new DisplayOperatorSwitchResult
            {
                Success = true,
                Message =
                    previousOperator == null
                        ? "تم تسجيل دخول المشغل بنجاح."
                        : "تم تبديل المشغل بنجاح.",
                RequiresPinChange = user.MustChangeOperatorPin,
                PreviousOperator = previousOperator,
                CurrentOperator = currentOperator,
            };
        }

        public bool SignOutDisplayOperator(
            string deviceId,
            out DisplayOperatorSessionInfo? previousOperator
        )
        {
            previousOperator = null;
            var normalizedDeviceId = UserAccountService.NormalizeDeviceId(deviceId);
            if (!_displayOperatorSessions.TryRemove(normalizedDeviceId, out var removedOperator))
            {
                return false;
            }

            previousOperator =
                VehiclePermitSystemWeb.Utilities.Users.UserCloneMapper.CloneDisplayOperatorSession(
                    removedOperator
                );
            using var db = _dbContextFactory.CreateDbContext();
            _permitAuditService.RecordUserActivity(
                db,
                removedOperator.Username,
                removedOperator.DisplayName,
                "DisplayOperatorSignOut",
                "خروج مشغل بوابة",
                $"تم تسجيل خروج المشغل من شاشة البوابة ({normalizedDeviceId}).",
                "DisplayController",
                removedOperator.Username,
                _systemClock.UtcNow
            );
            db.SaveChanges();
            return true;
        }

        public bool ChangeDisplayOperatorPin(
            string deviceId,
            string currentPin,
            string newPin,
            out string errorCode
        )
        {
            errorCode = string.Empty;
            var session = GetDisplayOperatorSession(deviceId);
            if (session == null)
            {
                errorCode = "operator_not_signed_in";
                return false;
            }

            var normalizedCurrentPin = (currentPin ?? string.Empty).Trim();
            var normalizedNewPin = (newPin ?? string.Empty).Trim();
            if (
                UserAccountService.IsOperatorPinValid(normalizedCurrentPin)
                && UserAccountService.IsOperatorPinValid(normalizedNewPin)
                && string.Equals(normalizedCurrentPin, normalizedNewPin, StringComparison.Ordinal)
            )
            {
                errorCode = "operator_pin_unchanged";
                return false;
            }

            if (
                !UserAccountService.IsOperatorPinValid(normalizedCurrentPin)
                || !UserAccountService.IsOperatorPinValid(normalizedNewPin)
            )
            {
                errorCode = "invalid_operator_pin";
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var user = db.UserAccounts.FirstOrDefault(u => u.Username == session.Username);
            if (user == null || !UserAccountService.VerifyOperatorPin(user, normalizedCurrentPin))
            {
                errorCode = "invalid_operator_pin";
                return false;
            }

            UserAccountService.SetOperatorPin(user, normalizedNewPin);
            user.MustChangeOperatorPin = false;
            db.SaveChanges();

            if (_displayOperatorSessions.TryGetValue(session.DeviceId, out var activeSession))
            {
                activeSession.MustChangePin = false;
                _displayOperatorSessions[session.DeviceId] = activeSession;
            }

            _permitAuditService.RecordUserActivity(
                db,
                user.Username,
                user.DisplayName,
                "DisplayOperatorPinChanged",
                "تغيير رمز مشغل البوابة",
                "تم تحديث الرمز السري الخاص بمشغل شاشة البوابة.",
                "DisplayController",
                user.Username,
                _systemClock.UtcNow
            );

            return true;
        }

        public IEnumerable<Department> GetDepartments()
        {
            using var db = _dbContextFactory.CreateDbContext();
            return DepartmentManagementMapper.GetDepartments(db);
        }

        public IEnumerable<Department> GetDepartments(
            string tenantId,
            bool ignoreTenantFilters = false
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var normalizedTenantId = string.IsNullOrWhiteSpace(tenantId)
                ? db.CurrentTenantId
                : tenantId.Trim();
            if (
                !ignoreTenantFilters
                && !string.Equals(
                    normalizedTenantId,
                    db.CurrentTenantId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                normalizedTenantId = db.CurrentTenantId;
            }

            return db
                .Departments.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(department => department.TenantId == normalizedTenantId)
                .OrderBy(department => department.Name)
                .ToList();
        }

        public IEnumerable<UserAccount> GetUsersByDepartment(string departmentName)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return DepartmentManagementMapper.GetUsersByDepartment(db, departmentName);
        }

        public Department? GetDepartment(int id)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return DepartmentManagementMapper.GetDepartment(db, id);
        }

        public bool UpsertDepartment(Department department)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var existing =
                department.Id > 0
                    ? db.Departments.FirstOrDefault(x => x.Id == department.Id)
                    : db.Departments.FirstOrDefault(x => x.Name == department.Name);

            if (department.Id <= 0 && existing != null)
            {
                return false;
            }

            if (
                department.Id > 0
                && db.Departments.Any(x => x.Id != department.Id && x.Name == department.Name)
            )
            {
                return false;
            }

            var managerDisplayName = string.Empty;
            if (!string.IsNullOrWhiteSpace(department.ManagerUsername))
            {
                managerDisplayName =
                    db.UserAccounts.AsNoTracking()
                        .FirstOrDefault(x => x.Username == department.ManagerUsername)
                        ?.DisplayName
                    ?? string.Empty;
            }

            if (existing == null)
            {
                db.Departments.Add(
                    new Department
                    {
                        Name = department.Name,
                        ManagerUsername = department.ManagerUsername,
                        ManagerDisplayName = managerDisplayName,
                        IsActive = department.IsActive,
                    }
                );
            }
            else
            {
                existing.Name = department.Name;
                existing.ManagerUsername = string.IsNullOrWhiteSpace(department.ManagerUsername)
                    ? existing.ManagerUsername
                    : department.ManagerUsername;
                existing.ManagerDisplayName = string.IsNullOrWhiteSpace(department.ManagerUsername)
                    ? existing.ManagerDisplayName
                    : managerDisplayName;
                existing.IsActive = department.IsActive;
            }

            db.SaveChanges();
            return true;
        }

        public bool SetDepartmentManager(int departmentId, string? managerUsername)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return DepartmentManagementMapper.SetDepartmentManager(
                db,
                departmentId,
                managerUsername
            );
        }

        public string? HandoverDepartmentManager(
            DepartmentManagerHandoverRequest request,
            string? recordedBy = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            return DepartmentManagementMapper.HandoverDepartmentManager(
                db,
                _permitAuditService,
                _systemClock,
                request,
                recordedBy
            );
        }

        public string? AssignGeneralManager(
            GeneralManagerAssignmentRequest request,
            string? recordedBy = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var targetTenantId = string.IsNullOrWhiteSpace(request.TenantId)
                ? db.CurrentTenantId
                : request.TenantId.Trim();
            if (
                !db.Tenants.Any(tenant =>
                    tenant.TenantId == targetTenantId && tenant.IsActive
                )
            )
            {
                return null;
            }

            var settings = db
                .AdministrationSettings.IgnoreQueryFilters()
                .Where(item => item.TenantId == targetTenantId)
                .OrderByDescending(item => item.Id == 1)
                .ThenBy(item => item.Id)
                .FirstOrDefault();
            if (settings == null)
            {
                settings = AdministrationSettingsService.BuildDefaultAdministrationSettings();
                settings.Id = GetNextAdministrationSettingsId(db);
                settings.TenantId = targetTenantId;
                db.AdministrationSettings.Add(settings);
            }

            if (
                string.IsNullOrWhiteSpace(settings.OrganizationName)
                || string.IsNullOrWhiteSpace(settings.DepartmentName)
            )
            {
                return null;
            }

            var currentGeneralManagerUsername = (
                settings.GeneralManagerUsername ?? string.Empty
            ).Trim();
            var currentGeneralManager = string.IsNullOrWhiteSpace(currentGeneralManagerUsername)
                ? null
                : db.UserAccounts.IgnoreQueryFilters().FirstOrDefault(user =>
                    user.TenantId == targetTenantId
                    && user.Username == currentGeneralManagerUsername
                );

            var normalizedSelectionMode =
                ManagerTransitionService.NormalizeGeneralManagerSelectionMode(
                    request.SelectionMode
                );
            var normalizedAssignmentType =
                ManagerTransitionService.NormalizeGeneralManagerAssignmentType(
                    request.AssignmentType
                );
            var normalizedPreviousAction =
                ManagerTransitionService.NormalizeGeneralManagerPreviousAction(
                    request.PreviousGeneralManagerAction
                );
            var replacementJobTitle = ManagerTransitionService.ResolveGeneralManagerJobTitle(
                normalizedAssignmentType,
                normalizedSelectionMode == GeneralManagerSelectionModes.CreateNew
                    ? request.NewUserJobTitle
                    : null
            );

            UserAccount nextGeneralManager;
            if (normalizedSelectionMode == GeneralManagerSelectionModes.CreateNew)
            {
                var newUsername = (request.NewUserUsername ?? string.Empty).Trim();
                var newFullName = (request.NewUserFullName ?? string.Empty).Trim();
                var newPhoneNumber = (request.NewUserPhoneNumber ?? string.Empty).Trim();
                var newEmail = (request.NewUserEmail ?? string.Empty).Trim();
                var newEmployeeNumber = (request.NewUserEmployeeNumber ?? string.Empty).Trim();
                var newPassword = request.NewUserPassword ?? string.Empty;
                if (
                    string.IsNullOrWhiteSpace(newUsername)
                    || string.IsNullOrWhiteSpace(newFullName)
                    || !ManagerTransitionService.IsSaudiMobileNumber(newPhoneNumber)
                    || string.IsNullOrWhiteSpace(newPassword)
                    || db
                        .UserAccounts.IgnoreQueryFilters()
                        .Any(user => user.Username == newUsername)
                )
                {
                    return null;
                }

                nextGeneralManager = new UserAccount
                {
                    TenantId = targetTenantId,
                    Username = newUsername,
                    DisplayName = newFullName,
                    FullName = newFullName,
                    Department = string.Empty,
                    JobTitle = replacementJobTitle,
                    PhoneNumber = newPhoneNumber,
                    Email = newEmail,
                    EmployeeNumber = newEmployeeNumber,
                    IsActive = request.NewUserIsActive,
                    Role = AppRoles.GeneralManager,
                    ManagerUsername = string.Empty,
                };

                UserPermissionService.NormalizePrivilegedAssignments(nextGeneralManager);
                AppPermissions.ApplyRoleDefaults(nextGeneralManager);
                UserAccountService.SetPassword(nextGeneralManager, newPassword);
                nextGeneralManager.MustChangePassword = request.NewUserMustChangePassword;
                db.UserAccounts.Add(nextGeneralManager);
            }
            else
            {
                var existingUsername = (request.ExistingUserUsername ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(existingUsername))
                {
                    return null;
                }

                nextGeneralManager = db.UserAccounts.IgnoreQueryFilters().FirstOrDefault(user =>
                    user.TenantId == targetTenantId && user.Username == existingUsername
                )!;
                if (nextGeneralManager == null || nextGeneralManager.IsSuperAdmin)
                {
                    return null;
                }

                if (
                    !string.IsNullOrWhiteSpace(currentGeneralManagerUsername)
                    && string.Equals(
                        currentGeneralManagerUsername,
                        nextGeneralManager.Username,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return null;
                }

                var normalizedNextGeneralManagerUsername = (
                    nextGeneralManager.Username ?? string.Empty
                )
                    .Trim()
                    .ToUpperInvariant();
                if (
                    db.Departments.IgnoreQueryFilters().Any(department =>
                        department.TenantId == targetTenantId
                        && !string.IsNullOrWhiteSpace(department.ManagerUsername)
                        && department.ManagerUsername.Trim().ToUpper()
                            == normalizedNextGeneralManagerUsername
                    )
                )
                {
                    return null;
                }

                nextGeneralManager.IsActive = true;
                nextGeneralManager.Role = AppRoles.GeneralManager;
                nextGeneralManager.JobTitle = replacementJobTitle;
                nextGeneralManager.ManagerUsername = string.Empty;
                UserPermissionService.NormalizePrivilegedAssignments(nextGeneralManager);
                AppPermissions.ApplyRoleDefaults(nextGeneralManager);
            }

            var nextGeneralManagerUsername = (nextGeneralManager.Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nextGeneralManagerUsername))
            {
                return null;
            }

            string previousGeneralManagerResult = string.Empty;
            if (currentGeneralManager != null)
            {
                previousGeneralManagerResult =
                    AdministrationSettingsService.ApplyPreviousGeneralManagerAction(
                        db,
                        currentGeneralManager,
                        normalizedPreviousAction,
                        request.PreviousGeneralManagerTargetDepartmentId,
                        targetTenantId
                    ) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(previousGeneralManagerResult))
                {
                    return null;
                }
            }

            settings.GeneralManagerUsername = nextGeneralManagerUsername;
            settings.ManagerName = nextGeneralManager.DisplayName;
            settings.ManagerTitle = nextGeneralManager.JobTitle;
            settings.ManagerPhoneNumber = nextGeneralManager.PhoneNumber;
            AdministrationSettingsService.NormalizeAdministrationSettings(settings);
            AdministrationSettingsService.StopOtherGeneralManagers(
                db,
                nextGeneralManagerUsername,
                targetTenantId
            );

            _permitAuditService.RecordUserActivity(
                db,
                nextGeneralManagerUsername,
                nextGeneralManager.DisplayName,
                "GeneralManagerAssigned",
                "تعيين المدير العام",
                $"تم تعيين {nextGeneralManager.DisplayName} مديرًا عامًا بصيغة {ManagerTransitionService.GetGeneralManagerAssignmentTypeLabel(normalizedAssignmentType)} عبر مسار {(normalizedSelectionMode == GeneralManagerSelectionModes.CreateNew ? "إنشاء جديد" : "اختيار مستخدم موجود")}.",
                nameof(UserAdminService),
                recordedBy,
                _systemClock.UtcNow,
                tenantId: targetTenantId
            );

            if (currentGeneralManager != null)
            {
                _permitAuditService.RecordUserActivity(
                    db,
                    currentGeneralManager.Username,
                    currentGeneralManager.DisplayName,
                    "GeneralManagerReleased",
                    "إنهاء ارتباط المدير العام السابق",
                    $"تم إنهاء تكليف {currentGeneralManager.DisplayName} كمدير عام بسبب {ManagerTransitionService.GetGeneralManagerPreviousActionLabel(normalizedPreviousAction)}. {previousGeneralManagerResult}",
                    nameof(UserAdminService),
                    recordedBy,
                    _systemClock.UtcNow,
                    tenantId: targetTenantId
                );
            }

            _permitAuditService.RecordUserActivity(
                db,
                nextGeneralManagerUsername,
                nextGeneralManager.DisplayName,
                "UpdateAdministrationGeneralManagerLink",
                "ربط المدير العام بالإدارة",
                $"تم تحديث بيانات الإدارة وربط المدير العام بالحساب {nextGeneralManagerUsername} تلقائيًا مع مزامنة الاسم والمسمى والجوال.",
                nameof(UserAdminService),
                recordedBy,
                _systemClock.UtcNow,
                tenantId: targetTenantId
            );

            db.SaveChanges();
            _memoryCache.Remove(BuildAdministrationSettingsCacheKey(targetTenantId));

            return currentGeneralManager == null
                ? $"تم تعيين {nextGeneralManager.DisplayName} مديرًا عامًا بنجاح وربطه بالإدارة تلقائيًا."
                : $"تم تبديل المدير العام إلى {nextGeneralManager.DisplayName} بنجاح، وتمت معالجة المدير السابق وفق الإجراء المحدد.";
        }

        public string? GetDepartmentDeleteBlockReason(int id)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return DepartmentManagementMapper.GetDepartmentDeleteBlockReason(db, id);
        }

        public bool DeleteDepartment(int id)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return DepartmentManagementMapper.DeleteDepartment(db, id);
        }

        public bool TransferDepartmentUsers(
            string sourceDepartmentName,
            string targetDepartmentName,
            IEnumerable<string> usernames,
            bool transferAll,
            string? recordedBy = null,
            string? replacementManagerUsername = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            return DepartmentManagementMapper.TransferDepartmentUsers(
                db,
                _permitAuditService,
                _systemClock,
                sourceDepartmentName,
                targetDepartmentName,
                usernames,
                transferAll,
                recordedBy,
                replacementManagerUsername
            );
        }

        public bool ValidateCredentials(
            string username,
            string password,
            out string displayName,
            out string role
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            return UserAccountService.ValidateCredentials(
                db,
                username,
                password,
                out displayName,
                out role
            );
        }

        public string CreateSession(string username)
        {
            return _userSessionService.CreateSession(username);
        }

        public bool ValidateSession(string sessionId, out string? username)
        {
            return _userSessionService.ValidateSession(sessionId, out username);
        }

        public bool RefreshSession(string sessionId)
        {
            return _userSessionService.RefreshSession(sessionId);
        }

        public void RemoveSession(string sessionId)
        {
            _userSessionService.RemoveSession(sessionId);
        }

        public bool IsClientIpAllowed(HttpContext context)
        {
            return _userSessionService.IsClientIpAllowed(context);
        }

        public void RecordUserActivity(
            string username,
            string displayName,
            string actionType,
            string actionLabel,
            string message,
            string source,
            string? recordedBy = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            _permitAuditService.RecordUserActivity(
                db,
                username,
                displayName,
                actionType,
                actionLabel,
                message,
                source,
                recordedBy,
                _systemClock.UtcNow
            );

            db.SaveChanges();
        }

        private void MigrateUsernameReferences(
            ApplicationDbContext db,
            string oldUsername,
            string newUsername,
            string newDisplayName
        )
        {
            foreach (
                var account in db.UserAccounts.Where(account =>
                    account.ManagerUsername == oldUsername
                )
            )
            {
                account.ManagerUsername = newUsername;
            }

            foreach (
                var department in db.Departments.Where(department =>
                    department.ManagerUsername == oldUsername
                )
            )
            {
                department.ManagerUsername = newUsername;
                department.ManagerDisplayName = newDisplayName;
            }

            var settings = AdministrationSettingsService.GetAdministrationSettingsRecord(db, 1);
            if (settings != null && settings.GeneralManagerUsername == oldUsername)
            {
                settings.GeneralManagerUsername = newUsername;
                settings.ManagerName = newDisplayName;
            }

            foreach (
                var delegation in db.Delegations.Where(delegation =>
                    delegation.DelegatorUsername == oldUsername
                    || delegation.DelegateeUsername == oldUsername
                    || delegation.CreatedByUsername == oldUsername
                    || delegation.LastUpdatedByUsername == oldUsername
                    || delegation.CancelledByUsername == oldUsername
                )
            )
            {
                if (delegation.DelegatorUsername == oldUsername)
                {
                    delegation.DelegatorUsername = newUsername;
                }

                if (delegation.DelegateeUsername == oldUsername)
                {
                    delegation.DelegateeUsername = newUsername;
                }

                if (delegation.CreatedByUsername == oldUsername)
                {
                    delegation.CreatedByUsername = newUsername;
                }

                if (delegation.LastUpdatedByUsername == oldUsername)
                {
                    delegation.LastUpdatedByUsername = newUsername;
                }

                if (delegation.CancelledByUsername == oldUsername)
                {
                    delegation.CancelledByUsername = newUsername;
                }
            }

            foreach (
                var activity in db.UserActivities.Where(activity =>
                    activity.Username == oldUsername || activity.RecordedBy == oldUsername
                )
            )
            {
                if (activity.Username == oldUsername)
                {
                    activity.Username = newUsername;
                }

                if (activity.RecordedBy == oldUsername)
                {
                    activity.RecordedBy = newUsername;
                }
            }

            foreach (
                var auditLog in db.AuditLogs.Where(auditLog =>
                    auditLog.Username == oldUsername
                    || auditLog.RecordedBy == oldUsername
                    || auditLog.ActualActorUsername == oldUsername
                    || auditLog.DelegatedFromUsername == oldUsername
                )
            )
            {
                if (auditLog.Username == oldUsername)
                {
                    auditLog.Username = newUsername;
                }

                if (auditLog.RecordedBy == oldUsername)
                {
                    auditLog.RecordedBy = newUsername;
                }

                if (auditLog.ActualActorUsername == oldUsername)
                {
                    auditLog.ActualActorUsername = newUsername;
                }

                if (auditLog.DelegatedFromUsername == oldUsername)
                {
                    auditLog.DelegatedFromUsername = newUsername;
                }
            }

            foreach (
                var permitActivity in db.PermitActivities.Where(activity =>
                    activity.RecordedBy == oldUsername
                    || activity.GateOperatorAccount == oldUsername
                )
            )
            {
                if (permitActivity.RecordedBy == oldUsername)
                {
                    permitActivity.RecordedBy = newUsername;
                }

                if (permitActivity.GateOperatorAccount == oldUsername)
                {
                    permitActivity.GateOperatorAccount = newUsername;
                }
            }

            foreach (var permit in db.Permits.Where(permit => permit.CreatedBy == oldUsername))
            {
                permit.CreatedBy = newUsername;
            }

            foreach (
                var visit in db.Visits.Where(visit => visit.VisitApproverUsername == oldUsername)
            )
            {
                visit.VisitApproverUsername = newUsername;
            }

            foreach (
                var session in db.SessionRecords.Where(session => session.Username == oldUsername)
            )
            {
                session.Username = newUsername;
            }
        }

        private void SyncDisplayOperatorSessionIdentity(
            string oldUsername,
            string newUsername,
            string displayName
        )
        {
            foreach (var entry in _displayOperatorSessions)
            {
                if (
                    !string.Equals(
                        entry.Value.Username,
                        oldUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                entry.Value.Username = newUsername;
                entry.Value.DisplayName = displayName;
            }
        }
    }
}
