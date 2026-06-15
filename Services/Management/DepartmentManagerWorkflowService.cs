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
using VehiclePermitSystemWeb.Services.Users;

namespace VehiclePermitSystemWeb.Services.Management
{
    public static class DepartmentManagerWorkflowService
    {
        public static void SynchronizeDepartmentManagerState(
            ApplicationDbContext db,
            UserAccount user,
            string? targetDepartmentName,
            bool shouldAssignDepartment
        )
        {
            var normalizedTargetDepartmentName = (targetDepartmentName ?? string.Empty).Trim();
            var affectedUsernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                user.Username,
            };

            var currentAssignments = db
                .Departments.AsEnumerable()
                .Where(department => department.ManagerUsername == user.Username)
                .ToList();

            Department? targetDepartment = null;
            if (
                shouldAssignDepartment && !string.IsNullOrWhiteSpace(normalizedTargetDepartmentName)
            )
            {
                targetDepartment = db
                    .Departments.AsEnumerable()
                    .FirstOrDefault(department =>
                        string.Equals(
                            department.Name,
                            normalizedTargetDepartmentName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
            }

            foreach (var assignment in currentAssignments)
            {
                if (targetDepartment != null && assignment.Id == targetDepartment.Id)
                {
                    continue;
                }

                assignment.ManagerUsername = string.Empty;
                assignment.ManagerDisplayName = string.Empty;
            }

            if (targetDepartment != null)
            {
                var previousManagerUsername = (
                    targetDepartment.ManagerUsername ?? string.Empty
                ).Trim();
                if (
                    !string.IsNullOrWhiteSpace(previousManagerUsername)
                    && !string.Equals(
                        previousManagerUsername,
                        user.Username,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    affectedUsernames.Add(previousManagerUsername);
                }

                targetDepartment.ManagerUsername = user.Username;
                targetDepartment.ManagerDisplayName = user.DisplayName;

                if (
                    string.Equals(
                        user.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    user.Department = targetDepartment.Name;
                }
            }

            RecalculateDepartmentManagerPermissions(db, affectedUsernames);
        }

        public static string? ApplyPreviousDepartmentManagerExitAction(
            ApplicationDbContext db,
            UserAccount currentManager,
            Department sourceDepartment,
            string exitAction,
            int targetDepartmentId,
            string? requestedRole
        )
        {
            switch (exitAction)
            {
                case DepartmentManagerExitActions.Retirement:
                case DepartmentManagerExitActions.Resignation:
                case DepartmentManagerExitActions.ExternalTransfer:
                    currentManager.IsActive = false;
                    currentManager.Role = AppRoles.Employee;
                    currentManager.Department = string.Empty;
                    currentManager.ManagerUsername = string.Empty;
                    currentManager.JobTitle = exitAction switch
                    {
                        DepartmentManagerExitActions.Retirement => "متقاعد (مؤرشف)",
                        DepartmentManagerExitActions.Resignation => "مستقيل (مؤرشف)",
                        _ => "منقول خارجيًا (مؤرشف)",
                    };
                    AppPermissions.ApplyRoleDefaults(currentManager);
                    return "تمت أرشفة الحساب وتعطيله مع الاحتفاظ بالسجل.";
                case DepartmentManagerExitActions.EndAssignment:
                    currentManager.Role = ManagerTransitionService.NormalizeHandoverRole(
                        requestedRole,
                        allowManager: false
                    );
                    currentManager.Department = sourceDepartment.Name;
                    currentManager.ManagerUsername = string.Empty;
                    currentManager.JobTitle = ManagerTransitionService.GetRoleJobTitle(
                        currentManager.Role
                    );
                    AppPermissions.ApplyRoleDefaults(currentManager);
                    return $"تمت إعادته داخل القسم بدور {AppRoles.GetDisplayName(currentManager.Role)} دون صلاحيات مدير القسم.";
                case DepartmentManagerExitActions.InternalTransfer:
                    var targetDepartment = db.Departments.FirstOrDefault(item =>
                        item.Id == targetDepartmentId && item.IsActive
                    );
                    if (targetDepartment == null || targetDepartment.Id == sourceDepartment.Id)
                    {
                        return null;
                    }

                    var nextRole = ManagerTransitionService.NormalizeHandoverRole(
                        requestedRole,
                        allowManager: true
                    );
                    currentManager.ManagerUsername = string.Empty;
                    currentManager.Role = nextRole;
                    currentManager.JobTitle = ManagerTransitionService.GetRoleJobTitle(nextRole);
                    AppPermissions.ApplyRoleDefaults(currentManager);

                    if (
                        string.Equals(
                            nextRole,
                            AppRoles.DepartmentManager,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        var targetHasManager = !string.IsNullOrWhiteSpace(
                            targetDepartment.ManagerUsername
                        );
                        if (targetHasManager)
                        {
                            return null;
                        }

                        SynchronizeDepartmentManagerState(
                            db,
                            currentManager,
                            targetDepartment.Name,
                            true
                        );
                        return $"تم نقله داخليًا مديرًا لقسم {targetDepartment.Name}.";
                    }

                    currentManager.Department = targetDepartment.Name;
                    return $"تم نقله داخليًا إلى قسم {targetDepartment.Name} بدور {AppRoles.GetDisplayName(nextRole)}.";
                default:
                    return null;
            }
        }

        public static void RecalculateDepartmentManagerPermissions(
            ApplicationDbContext db,
            IEnumerable<string> usernames
        )
        {
            foreach (
                var username in usernames
                    .Select(value => (value ?? string.Empty).Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                var account = db.UserAccounts.FirstOrDefault(user => user.Username == username);
                if (account == null)
                {
                    continue;
                }

                if (
                    !string.Equals(
                        account.Role,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                var hasMatchingDepartmentAssignment =
                    account.IsActive
                    && !string.IsNullOrWhiteSpace(account.Department)
                    && db.Departments.AsEnumerable()
                        .Any(department =>
                            department.IsActive
                            && string.Equals(
                                department.ManagerUsername,
                                account.Username,
                                StringComparison.OrdinalIgnoreCase
                            )
                            && string.Equals(
                                department.Name,
                                account.Department,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                if (hasMatchingDepartmentAssignment)
                {
                    AppPermissions.ApplyRoleDefaults(account);
                    continue;
                }

                UserPermissionService.ClearDepartmentManagerPermissions(account);
            }
        }
    }
}
