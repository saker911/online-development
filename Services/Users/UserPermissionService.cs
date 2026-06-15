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
    public static class UserPermissionService
    {
        public static void NormalizePrivilegedAssignments(UserAccount user)
        {
            if (user.IsSuperAdmin)
            {
                user.Role = AppRoles.SystemAdmin;
                user.Department = string.Empty;
                user.ManagerUsername = string.Empty;
                return;
            }

            if (
                !string.Equals(
                    user.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            user.Department = string.Empty;
            user.ManagerUsername = string.Empty;
        }

        public static void ClearDepartmentManagerPermissions(UserAccount user)
        {
            user.CanViewDashboard = false;
            user.CanViewPermits = false;
            user.CanViewVisitorPermits = false;
            user.CanCreatePermit = false;
            user.CanCreateVisitorPermit = false;
            user.CanEditPermit = false;
            user.CanEditVisitorPermit = false;
            user.CanApprovePermit = false;
            user.CanApproveLeaveRequest = false;
            user.CanStopPermit = false;
            user.CanReviewUnauthorizedExit = false;
            user.CanViewVisits = false;
            user.CanCreateVisit = false;
            user.CanEditVisit = false;
            user.CanApproveDetainedVisit = false;
            user.CanApproveVisits = false;
            user.CanScanOperations = false;
            user.CanViewDisplays = false;
            user.CanManageUsers = false;
            user.CanManageDepartments = false;
            user.CanManageAdministration = false;
            user.CanManageDelegations = false;
        }
    }
}
