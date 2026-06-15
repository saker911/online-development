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
using VehiclePermitSystemWeb.Services.Management;

namespace VehiclePermitSystemWeb.Services.Administration
{
    public static class AdministrationSettingsService
    {
        public static AdministrationSettings? GetAdministrationSettingsRecord(
            ApplicationDbContext db,
            int id
        )
        {
            return db.AdministrationSettings.FirstOrDefault(settings => settings.Id == id);
        }

        public static AdministrationSettings BuildDefaultAdministrationSettings()
        {
            return new AdministrationSettings
            {
                Id = 1,
                DisplayAccessKey = DisplayAccessDefaults.CreateAccessKey(),
            };
        }

        public static AdministrationSettings CloneAdministrationSettings(
            AdministrationSettings settings
        )
        {
            return new AdministrationSettings
            {
                Id = settings.Id,
                IsInitialSetupCompleted = settings.IsInitialSetupCompleted,
                OrganizationName = settings.OrganizationName,
                DepartmentName = settings.DepartmentName,
                Address = settings.Address,
                Phone = settings.Phone,
                Email = settings.Email,
                GeneralManagerUsername = settings.GeneralManagerUsername,
                ManagerName = settings.ManagerName,
                ManagerTitle = settings.ManagerTitle,
                ManagerPhoneNumber = settings.ManagerPhoneNumber,
                SignatureText = settings.SignatureText,
                LogoPath = settings.LogoPath,
                SignatureImagePath = settings.SignatureImagePath,
                DisplayBaseUrl = settings.DisplayBaseUrl,
                DisplayAccessKey = settings.DisplayAccessKey,
                AllowedClientIpRanges = settings.AllowedClientIpRanges,
                WorkStartTime = settings.WorkStartTime,
                WorkEndTime = settings.WorkEndTime,
                AttendanceGraceMinutes = settings.AttendanceGraceMinutes,
                WorkEndExitGraceMinutes = settings.WorkEndExitGraceMinutes,
                LateReturnGraceMinutes = settings.LateReturnGraceMinutes,
                OfficialWorkDaysCsv = settings.OfficialWorkDaysCsv,
            };
        }

        public static void NormalizeAdministrationSettings(AdministrationSettings settings)
        {
            settings.OrganizationName = (settings.OrganizationName ?? string.Empty).Trim();
            settings.DepartmentName = (settings.DepartmentName ?? string.Empty).Trim();
            settings.Address = (settings.Address ?? string.Empty).Trim();
            settings.Phone = (settings.Phone ?? string.Empty).Trim();
            settings.Email = (settings.Email ?? string.Empty).Trim();
            settings.GeneralManagerUsername = (
                settings.GeneralManagerUsername ?? string.Empty
            ).Trim();
            settings.ManagerName = (settings.ManagerName ?? string.Empty).Trim();
            settings.ManagerTitle = (settings.ManagerTitle ?? string.Empty).Trim();
            settings.ManagerPhoneNumber = (settings.ManagerPhoneNumber ?? string.Empty).Trim();
            settings.SignatureText = (settings.SignatureText ?? string.Empty).Trim();
            settings.DisplayBaseUrl = (settings.DisplayBaseUrl ?? string.Empty).Trim();
            settings.DisplayAccessKey =
                string.IsNullOrWhiteSpace(settings.DisplayAccessKey)
                || DisplayAccessDefaults.LooksLikePlaceholder(settings.DisplayAccessKey)
                    ? DisplayAccessDefaults.CreateAccessKey()
                    : settings.DisplayAccessKey.Trim();
            settings.AllowedClientIpRanges = (
                settings.AllowedClientIpRanges ?? string.Empty
            ).Trim();
            settings.OfficialWorkDaysCsv = AdministrationWorkSchedule.NormalizeOfficialWorkDaysCsv(
                settings.OfficialWorkDaysCsv
            );
            if (settings.AttendanceGraceMinutes < 0)
            {
                settings.AttendanceGraceMinutes = 0;
            }
            if (settings.WorkEndExitGraceMinutes < 0)
            {
                settings.WorkEndExitGraceMinutes = 0;
            }
            if (settings.LateReturnGraceMinutes < 0)
            {
                settings.LateReturnGraceMinutes = 0;
            }
        }

        public static void EnsureDepartmentExists(ApplicationDbContext db, string? departmentName)
        {
            var normalizedDepartmentName = (departmentName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedDepartmentName))
            {
                return;
            }

            if (db.Departments.Any(department => department.Name == normalizedDepartmentName))
            {
                return;
            }

            db.Departments.Add(new Department { Name = normalizedDepartmentName, IsActive = true });
        }

        public static void ApplyAdministrationGeneralManagerLink(
            ApplicationDbContext db,
            AdministrationSettings settings
        )
        {
            var normalizedUsername = (settings.GeneralManagerUsername ?? string.Empty).Trim();
            var generalManager = string.IsNullOrWhiteSpace(normalizedUsername)
                ? null
                : ResolveAdministrationGeneralManagerCandidate(db, normalizedUsername);

            if (!IsEligibleGeneralManager(generalManager))
            {
                generalManager = GetAdministrationGeneralManagerCandidates(db)
                    .OrderBy(user => user.DisplayName)
                    .ThenBy(user => user.Username)
                    .FirstOrDefault();
            }

            if (!IsEligibleGeneralManager(generalManager))
            {
                settings.GeneralManagerUsername = string.Empty;
                settings.ManagerName = string.Empty;
                settings.ManagerTitle = string.Empty;
                settings.ManagerPhoneNumber = string.Empty;
                NormalizeAdministrationSettings(settings);
                return;
            }

            settings.GeneralManagerUsername = generalManager!.Username;
            settings.ManagerName = generalManager.DisplayName;
            settings.ManagerTitle = generalManager.JobTitle;
            settings.ManagerPhoneNumber = generalManager.PhoneNumber;
            NormalizeAdministrationSettings(settings);
        }

        public static void SyncAdministrationGeneralManagerForUser(
            ApplicationDbContext db,
            UserAccount user,
            bool forceLink,
            bool isLinkedGeneralManager = false
        )
        {
            var settings = GetAdministrationSettingsRecord(db, 1);
            if (settings == null)
            {
                settings = BuildDefaultAdministrationSettings();
                db.AdministrationSettings.Add(settings);
            }

            var currentLinkedUsername = (settings.GeneralManagerUsername ?? string.Empty).Trim();
            var currentLinkedUser = string.IsNullOrWhiteSpace(currentLinkedUsername)
                ? null
                : ResolveAdministrationGeneralManagerCandidate(db, currentLinkedUsername);
            var hasEligibleLinkedUser = IsEligibleGeneralManager(currentLinkedUser);

            if (IsEligibleGeneralManager(user))
            {
                if (forceLink || isLinkedGeneralManager || !hasEligibleLinkedUser)
                {
                    settings.GeneralManagerUsername = user.Username;
                    settings.ManagerName = user.DisplayName;
                    settings.ManagerTitle = user.JobTitle;
                    settings.ManagerPhoneNumber = user.PhoneNumber;
                    NormalizeAdministrationSettings(settings);
                    StopOtherGeneralManagers(db, user.Username);
                }

                return;
            }

            if (!isLinkedGeneralManager)
            {
                return;
            }

            settings.GeneralManagerUsername = string.Empty;
            ApplyAdministrationGeneralManagerLink(db, settings);
        }

        public static UserAccount? ResolveAdministrationGeneralManagerCandidate(
            ApplicationDbContext db,
            string username
        )
        {
            var normalizedUsername = (username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                return null;
            }

            var trackedUser = db
                .ChangeTracker.Entries<UserAccount>()
                .Where(entry => entry.State != EntityState.Deleted)
                .Select(entry => entry.Entity)
                .FirstOrDefault(user =>
                    string.Equals(
                        user.Username,
                        normalizedUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (trackedUser != null)
            {
                return trackedUser;
            }

            return db
                .UserAccounts.AsNoTracking()
                .FirstOrDefault(user => user.Username == normalizedUsername);
        }

        public static IEnumerable<UserAccount> GetAdministrationGeneralManagerCandidates(
            ApplicationDbContext db
        )
        {
            var trackedUsers = db
                .ChangeTracker.Entries<UserAccount>()
                .Where(entry => entry.State != EntityState.Deleted)
                .Select(entry => entry.Entity)
                .ToList();

            var trackedUsernames = trackedUsers
                .Select(user => user.Username)
                .Where(username => !string.IsNullOrWhiteSpace(username))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var databaseUsers = db
                .UserAccounts.AsNoTracking()
                .Where(user => !trackedUsernames.Contains(user.Username))
                .ToList();

            return trackedUsers.Concat(databaseUsers).Where(IsEligibleGeneralManager);
        }

        public static bool IsEligibleGeneralManager(UserAccount? user)
        {
            return user != null
                && user.IsActive
                && string.Equals(
                    user.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public static bool IsAdministrationGeneralManager(ApplicationDbContext db, string username)
        {
            var settings = GetAdministrationSettingsRecord(db, 1);
            return settings != null
                && string.Equals(
                    settings.GeneralManagerUsername,
                    username,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public static string? ApplyPreviousGeneralManagerAction(
            ApplicationDbContext db,
            UserAccount currentGeneralManager,
            string previousAction,
            int targetDepartmentId
        )
        {
            var targetDepartment = db.Departments.FirstOrDefault(item =>
                item.Id == targetDepartmentId && item.IsActive
            );

            switch (previousAction)
            {
                case GeneralManagerPreviousActions.InternalTransfer:
                    if (targetDepartment == null)
                    {
                        return null;
                    }

                    currentGeneralManager.IsActive = false;
                    currentGeneralManager.Role = AppRoles.Employee;
                    currentGeneralManager.Department = targetDepartment.Name;
                    currentGeneralManager.ManagerUsername = string.Empty;
                    currentGeneralManager.JobTitle = "موظف";
                    AppPermissions.ApplyRoleDefaults(currentGeneralManager);
                    return $"تم نقل المدير العام السابق إلى قسم {targetDepartment.Name} كموظف مع إيقافه عن المهام الإدارية.";
                case GeneralManagerPreviousActions.ExternalTransfer:
                    currentGeneralManager.IsActive = false;
                    currentGeneralManager.Role = AppRoles.Employee;
                    currentGeneralManager.Department = string.Empty;
                    currentGeneralManager.ManagerUsername = string.Empty;
                    currentGeneralManager.JobTitle = "مدير عام منقول خارجيًا (مؤرشف)";
                    AppPermissions.ApplyRoleDefaults(currentGeneralManager);
                    return "تمت أرشفة المدير العام السابق كمنقول خارجيًا وتعطيل الحساب.";
                case GeneralManagerPreviousActions.Retirement:
                    currentGeneralManager.IsActive = false;
                    currentGeneralManager.Role = AppRoles.Employee;
                    currentGeneralManager.Department = string.Empty;
                    currentGeneralManager.ManagerUsername = string.Empty;
                    currentGeneralManager.JobTitle = "مدير عام متقاعد (مؤرشف)";
                    AppPermissions.ApplyRoleDefaults(currentGeneralManager);
                    return "تمت أرشفة المدير العام السابق كمتقاعد وتعطيل الحساب.";
                case GeneralManagerPreviousActions.Resignation:
                    currentGeneralManager.IsActive = false;
                    currentGeneralManager.Role = AppRoles.Employee;
                    currentGeneralManager.Department = string.Empty;
                    currentGeneralManager.ManagerUsername = string.Empty;
                    currentGeneralManager.JobTitle = "مدير عام مستقيل (مؤرشف)";
                    AppPermissions.ApplyRoleDefaults(currentGeneralManager);
                    return "تمت أرشفة المدير العام السابق كمستقيل وتعطيل الحساب.";
                case GeneralManagerPreviousActions.Archive:
                    currentGeneralManager.IsActive = false;
                    currentGeneralManager.Role = AppRoles.Employee;
                    currentGeneralManager.Department = string.Empty;
                    currentGeneralManager.ManagerUsername = string.Empty;
                    currentGeneralManager.JobTitle = "مدير عام مؤرشف";
                    AppPermissions.ApplyRoleDefaults(currentGeneralManager);
                    return "تمت أرشفة المدير العام السابق وتعطيل الحساب.";
                case GeneralManagerPreviousActions.ReturnToEmployee:
                case GeneralManagerPreviousActions.EndAssignment:
                default:
                    currentGeneralManager.IsActive = false;
                    currentGeneralManager.Role = AppRoles.Employee;
                    currentGeneralManager.Department = string.Empty;
                    currentGeneralManager.ManagerUsername = string.Empty;
                    currentGeneralManager.JobTitle = "موظف";
                    AppPermissions.ApplyRoleDefaults(currentGeneralManager);
                    return "تم إنهاء تكليف المدير العام السابق وتعطيل الحساب مع سحب صلاحيات المدير العام.";
            }
        }

        public static int StopOtherGeneralManagers(
            ApplicationDbContext db,
            string activeGeneralManagerUsername
        )
        {
            var normalizedActiveUsername = (activeGeneralManagerUsername ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedActiveUsername))
            {
                return 0;
            }

            var duplicates = db
                .UserAccounts.Where(user => !user.IsSuperAdmin)
                .ToList()
                .Where(user =>
                    !user.IsSuperAdmin
                    && string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !string.Equals(
                        user.Username,
                        normalizedActiveUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList();

            foreach (var duplicate in duplicates)
            {
                duplicate.IsActive = false;
                duplicate.Role = AppRoles.Employee;
                duplicate.Department = string.Empty;
                duplicate.ManagerUsername = string.Empty;
                duplicate.JobTitle = string.IsNullOrWhiteSpace(duplicate.JobTitle)
                    ? "مدير عام سابق (موقوف)"
                    : $"{duplicate.JobTitle} - مدير عام سابق (موقوف)";
                AppPermissions.ApplyRoleDefaults(duplicate);
            }

            return duplicates.Count;
        }
    }
}
