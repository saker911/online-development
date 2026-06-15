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
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Utilities.Permits
{
    public static class PermitDepartmentApprovalRoutesBuilder
    {
        public static Dictionary<string, string> BuildDepartmentApprovalRoutes(
            IUserAdminService userAdminService,
            IEnumerable<Department> departments
        )
        {
            var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var departmentList = departments.ToList();

            foreach (var department in departmentList)
            {
                var route = ResolveDepartmentApprovalRoute(
                    userAdminService,
                    department,
                    departmentList
                );
                if (
                    !string.IsNullOrWhiteSpace(department.Name) && !string.IsNullOrWhiteSpace(route)
                )
                {
                    routes[department.Name.Trim()] = route;
                }
            }

            return routes;
        }

        private static string ResolveDepartmentApprovalRoute(
            IUserAdminService userAdminService,
            Department? department,
            IReadOnlyCollection<Department> departments
        )
        {
            if (department == null || string.IsNullOrWhiteSpace(department.Name))
            {
                return string.Empty;
            }

            var departmentManager = ResolveDepartmentManager(
                userAdminService,
                department.ManagerUsername
            );
            if (!string.IsNullOrWhiteSpace(departmentManager?.DisplayName))
            {
                return departmentManager.DisplayName;
            }

            var generalManager = ResolveGeneralManager(userAdminService);

            if (!string.IsNullOrWhiteSpace(generalManager?.DisplayName))
            {
                return generalManager.DisplayName;
            }

            return department.ManagerDisplayName?.Trim()
                ?? departments
                    .FirstOrDefault(x =>
                        string.Equals(x.Name, department.Name, StringComparison.OrdinalIgnoreCase)
                    )
                    ?.ManagerDisplayName?.Trim()
                ?? string.Empty;
        }

        private static UserAccount? ResolveDepartmentManager(
            IUserAdminService userAdminService,
            string? username
        )
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var user = userAdminService.GetUserAccount(username);
            if (user == null || !user.IsActive)
            {
                return null;
            }

            return user;
        }

        private static UserAccount? ResolveGeneralManager(IUserAdminService userAdminService)
        {
            var settings = userAdminService.GetAdministrationSettings();
            if (!string.IsNullOrWhiteSpace(settings.GeneralManagerUsername))
            {
                var linkedGeneralManager = userAdminService.GetUserAccount(
                    settings.GeneralManagerUsername
                );
                if (
                    linkedGeneralManager != null
                    && linkedGeneralManager.IsActive
                    && linkedGeneralManager.CanApprovePermit
                    && string.Equals(
                        linkedGeneralManager.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return linkedGeneralManager;
                }
            }

            return userAdminService
                .GetAllUsers()
                .FirstOrDefault(user =>
                    user.IsActive
                    && string.Equals(
                        user.Role,
                        AppRoles.GeneralManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && user.CanApprovePermit
                );
        }
    }
}
