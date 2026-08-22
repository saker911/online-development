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
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Backup;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Delegations;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Utilities.Administration
{
    public static class DepartmentManagementMapper
    {
        public static IEnumerable<Department> GetDepartments(ApplicationDbContext db)
        {
            return db.Departments.AsNoTracking().OrderBy(x => x.Name).ToList();
        }

        public static IEnumerable<UserAccount> GetUsersByDepartment(
            ApplicationDbContext db,
            string departmentName
        )
        {
            var normalizedDepartmentName = (departmentName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedDepartmentName))
            {
                return Array.Empty<UserAccount>();
            }

            return db
                .UserAccounts.AsNoTracking()
                .Where(user => user.Department == normalizedDepartmentName)
                .OrderBy(user => user.FullName)
                .ThenBy(user => user.Username)
                .ToList();
        }

        public static Department? GetDepartment(ApplicationDbContext db, int id)
        {
            return db.Departments.AsNoTracking().FirstOrDefault(x => x.Id == id);
        }

        public static string? GetDepartmentDeleteBlockReason(ApplicationDbContext db, int id)
        {
            var existing = db.Departments.AsNoTracking().FirstOrDefault(x => x.Id == id);
            if (existing == null)
            {
                return null;
            }

            var departmentName = existing.Name.Trim();
            var reasons = new List<string>();

            var userCount = db.UserAccounts.Count(x => x.Department == departmentName);
            if (userCount > 0)
            {
                reasons.Add($"{userCount} موظف(ين)");
            }

            var adminCount = db.AdministrationSettings.Count(x =>
                x.DepartmentName == departmentName
            );
            if (adminCount > 0)
            {
                reasons.Add("إعدادات الإدارة");
            }

            return reasons.Count == 0 ? null : $"مرتبط بـ {string.Join(" و ", reasons)}";
        }

        public static bool DeleteDepartment(ApplicationDbContext db, int id)
        {
            var existing = db.Departments.FirstOrDefault(x => x.Id == id);
            if (existing == null)
            {
                return false;
            }

            var departmentName = existing.Name.Trim();
            var isReferenced =
                db.UserAccounts.Any(x => x.Department == departmentName)
                || db.AdministrationSettings.Any(x => x.DepartmentName == departmentName);

            if (isReferenced)
            {
                return false;
            }

            db.Departments.Remove(existing);
            db.SaveChanges();
            return true;
        }

        public static bool SetDepartmentManager(
            ApplicationDbContext db,
            int departmentId,
            string? managerUsername
        )
        {
            var existing = db.Departments.FirstOrDefault(x => x.Id == departmentId);
            if (existing == null)
            {
                return false;
            }

            var normalizedManagerUsername = (managerUsername ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedManagerUsername))
            {
                var previousManagerUsername = existing.ManagerUsername;
                existing.ManagerUsername = string.Empty;
                existing.ManagerDisplayName = string.Empty;

                if (!string.IsNullOrWhiteSpace(previousManagerUsername))
                {
                    DepartmentManagerWorkflowService.RecalculateDepartmentManagerPermissions(
                        db,
                        new[] { previousManagerUsername }
                    );
                }

                db.SaveChanges();
                return true;
            }

            var selectedManager = db.UserAccounts.FirstOrDefault(x =>
                x.Username == normalizedManagerUsername
            );
            if (selectedManager == null)
            {
                return false;
            }

            if (
                !string.Equals(
                    selectedManager.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            selectedManager.Department = existing.Name;

            DepartmentManagerWorkflowService.SynchronizeDepartmentManagerState(
                db,
                selectedManager,
                existing.Name,
                true
            );

            db.SaveChanges();
            return true;
        }

        public static string? HandoverDepartmentManager(
            ApplicationDbContext db,
            IPermitAuditService permitAuditService,
            ISystemClock systemClock,
            DepartmentManagerHandoverRequest request,
            string? recordedBy = null
        )
        {
            var department = db.Departments.FirstOrDefault(item => item.Id == request.DepartmentId);
            if (department == null || !department.IsActive)
            {
                return null;
            }

            var currentManagerUsername = (department.ManagerUsername ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(currentManagerUsername))
            {
                return null;
            }

            var currentManager = db.UserAccounts.FirstOrDefault(user =>
                user.Username == currentManagerUsername
            );
            if (currentManager == null || currentManager.IsSuperAdmin)
            {
                return null;
            }

            var normalizedAssignmentType = ManagerTransitionService.NormalizeManagerAssignmentType(
                request.AssignmentType
            );
            var normalizedExitAction = ManagerTransitionService.NormalizeManagerExitAction(
                request.ExitAction
            );
            var replacementJobTitle = ManagerTransitionService.GetDepartmentManagerJobTitle(
                normalizedAssignmentType
            );
            var replacementTemporaryPassword = string.Empty;

            UserAccount replacementManager;
            if (request.CreateNewManager)
            {
                var newManagerUsername = (request.NewManagerUsername ?? string.Empty).Trim();
                var newManagerFullName = (request.NewManagerFullName ?? string.Empty).Trim();
                var newManagerPhoneNumber = (request.NewManagerPhoneNumber ?? string.Empty).Trim();
                if (
                    string.IsNullOrWhiteSpace(newManagerUsername)
                    || !NewAccountUsernameValidator.IsValid(newManagerUsername)
                    || string.IsNullOrWhiteSpace(newManagerFullName)
                    || !ManagerTransitionService.IsSaudiMobileNumber(newManagerPhoneNumber)
                    || db.UserAccounts.Any(user => user.Username == newManagerUsername)
                )
                {
                    return null;
                }

                replacementTemporaryPassword = UserAccountService.GenerateTemporaryPassword();
                replacementManager = new UserAccount
                {
                    Username = newManagerUsername,
                    DisplayName = newManagerFullName,
                    FullName = newManagerFullName,
                    Department = department.Name,
                    JobTitle = replacementJobTitle,
                    PhoneNumber = newManagerPhoneNumber,
                    Email = string.Empty,
                    IsActive = true,
                    Role = AppRoles.DepartmentManager,
                    ManagerUsername = string.Empty,
                };

                UserPermissionService.NormalizePrivilegedAssignments(replacementManager);
                AppPermissions.ApplyRoleDefaults(replacementManager);
                UserAccountService.SetPassword(replacementManager, replacementTemporaryPassword);
                replacementManager.MustChangePassword = true;
                db.UserAccounts.Add(replacementManager);
            }
            else
            {
                var existingManagerUsername = (
                    request.ExistingManagerUsername ?? string.Empty
                ).Trim();
                if (
                    string.IsNullOrWhiteSpace(existingManagerUsername)
                    || string.Equals(
                        existingManagerUsername,
                        currentManager.Username,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return null;
                }

                replacementManager = db.UserAccounts.FirstOrDefault(user =>
                    user.Username == existingManagerUsername
                )!;
                if (
                    replacementManager == null
                    || !replacementManager.IsActive
                    || replacementManager.IsSuperAdmin
                    || string.Equals(
                        replacementManager.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return null;
                }

                var otherManagedDepartment = db.Departments.FirstOrDefault(item =>
                    item.Id != department.Id && item.ManagerUsername == replacementManager.Username
                );
                if (otherManagedDepartment != null)
                {
                    return null;
                }

                replacementManager.Role = AppRoles.DepartmentManager;
                replacementManager.Department = department.Name;
                replacementManager.JobTitle = replacementJobTitle;
                replacementManager.ManagerUsername = string.Empty;
                UserPermissionService.NormalizePrivilegedAssignments(replacementManager);
                AppPermissions.ApplyRoleDefaults(replacementManager);
            }

            DepartmentManagerWorkflowService.SynchronizeDepartmentManagerState(
                db,
                replacementManager,
                department.Name,
                true
            );

            var oldManagerFinalState =
                DepartmentManagerWorkflowService.ApplyPreviousDepartmentManagerExitAction(
                    db,
                    currentManager,
                    department,
                    normalizedExitAction,
                    request.PreviousManagerTargetDepartmentId,
                    request.PreviousManagerNewRole
                );
            if (oldManagerFinalState == null)
            {
                return null;
            }

            permitAuditService.RecordUserActivity(
                db,
                replacementManager.Username,
                replacementManager.DisplayName,
                "DepartmentManagerAssigned",
                "تسليم إدارة القسم",
                $"تم إسناد إدارة قسم {department.Name} إلى {replacementManager.DisplayName} بنوع تكليف {ManagerTransitionService.GetManagerAssignmentTypeLabel(normalizedAssignmentType)}.",
                nameof(UserAdminService),
                recordedBy,
                systemClock.UtcNow
            );
            permitAuditService.RecordUserActivity(
                db,
                currentManager.Username,
                currentManager.DisplayName,
                "DepartmentManagerReleased",
                "إنهاء ارتباط مدير القسم",
                $"تم إنهاء ارتباط {currentManager.DisplayName} بقسم {department.Name} بسبب {ManagerTransitionService.GetManagerExitActionLabel(normalizedExitAction)}. {oldManagerFinalState}",
                nameof(UserAdminService),
                recordedBy,
                systemClock.UtcNow
            );

            db.SaveChanges();

            var successMessage =
                $"تم تسليم إدارة قسم {department.Name} إلى {replacementManager.DisplayName} وسحب صلاحيات المدير من {currentManager.DisplayName}.";
            if (!string.IsNullOrWhiteSpace(replacementTemporaryPassword))
            {
                successMessage =
                    $"{successMessage} كلمة المرور المؤقتة للمدير الجديد هي {replacementTemporaryPassword} ويجب تغييرها عند أول دخول.";
            }

            return successMessage;
        }

        public static bool TransferDepartmentUsers(
            ApplicationDbContext db,
            IPermitAuditService permitAuditService,
            ISystemClock systemClock,
            string sourceDepartmentName,
            string targetDepartmentName,
            IEnumerable<string> usernames,
            bool transferAll,
            string? recordedBy = null,
            string? replacementManagerUsername = null
        )
        {
            var sourceName = (sourceDepartmentName ?? string.Empty).Trim();
            var targetName = (targetDepartmentName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName))
            {
                return false;
            }

            if (string.Equals(sourceName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var sourceUsers = db.UserAccounts.Where(user => user.Department == sourceName).ToList();
            if (sourceUsers.Count == 0)
            {
                return false;
            }

            var selectedUsernames = transferAll
                ? sourceUsers
                    .Select(user => user.Username)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : (usernames ?? Array.Empty<string>())
                    .Select(username => (username ?? string.Empty).Trim())
                    .Where(username => !string.IsNullOrWhiteSpace(username))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (selectedUsernames.Count == 0)
            {
                return false;
            }

            var sourceDepartment = db.Departments.FirstOrDefault(department =>
                department.Name == sourceName
            );
            if (sourceDepartment == null)
            {
                return false;
            }

            var targetDepartmentExists = db.Departments.Any(department =>
                department.Name == targetName
            );
            if (!targetDepartmentExists)
            {
                return false;
            }

            var movedUsers = sourceUsers
                .Where(user => selectedUsernames.Contains(user.Username))
                .ToList();

            if (movedUsers.Count == 0)
            {
                return false;
            }

            var sourceManagerUsername = (sourceDepartment.ManagerUsername ?? string.Empty).Trim();
            var isSourceManagerBeingTransferred =
                !string.IsNullOrWhiteSpace(sourceManagerUsername)
                && movedUsers.Any(user =>
                    string.Equals(
                        user.Username,
                        sourceManagerUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (isSourceManagerBeingTransferred)
            {
                var normalizedReplacementManagerUsername = (
                    replacementManagerUsername ?? string.Empty
                ).Trim();
                if (
                    string.IsNullOrWhiteSpace(normalizedReplacementManagerUsername)
                    || string.Equals(
                        normalizedReplacementManagerUsername,
                        sourceManagerUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || movedUsers.Any(user =>
                        string.Equals(
                            user.Username,
                            normalizedReplacementManagerUsername,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                {
                    return false;
                }

                var replacementManager = db.UserAccounts.FirstOrDefault(user =>
                    user.Username == normalizedReplacementManagerUsername
                );
                if (
                    replacementManager == null
                    || !replacementManager.IsActive
                    || !string.Equals(
                        replacementManager.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return false;
                }

                DepartmentManagerWorkflowService.SynchronizeDepartmentManagerState(
                    db,
                    replacementManager,
                    sourceDepartment.Name,
                    true
                );
            }

            var targetManagerDisplayName =
                db.Departments.AsNoTracking()
                    .FirstOrDefault(department => department.Name == targetName)
                    ?.ManagerDisplayName
                ?? string.Empty;

            foreach (var user in movedUsers)
            {
                var oldDepartment = user.Department;
                user.Department = targetName;
                if (!string.IsNullOrWhiteSpace(user.FullName))
                {
                    permitAuditService.RecordUserActivity(
                        db,
                        user.Username,
                        user.DisplayName,
                        "DepartmentTransfer",
                        "نقل موظف بين الأقسام",
                        $"تم نقل الموظف {user.DisplayName} من قسم {oldDepartment} إلى قسم {targetName}.",
                        nameof(UserAdminService),
                        recordedBy,
                        systemClock.UtcNow
                    );
                }
            }

            if (!string.IsNullOrWhiteSpace(targetManagerDisplayName))
            {
                permitAuditService.RecordUserActivity(
                    db,
                    string.Empty,
                    targetManagerDisplayName,
                    "DepartmentTransferNotice",
                    "إشعار مدير القسم",
                    $"تمت إضافة موظفين إلى قسم {targetName}.",
                    nameof(UserAdminService),
                    recordedBy,
                    systemClock.UtcNow
                );
            }

            db.SaveChanges();
            return true;
        }
    }
}
