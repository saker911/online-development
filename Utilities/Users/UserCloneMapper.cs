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

namespace VehiclePermitSystemWeb.Utilities.Users
{
    public static class UserCloneMapper
    {
        public static UserAccount CloneUserAccount(UserAccount user)
        {
            return new UserAccount
            {
                Username = user.Username,
                PasswordHash = user.PasswordHash,
                PasswordSalt = user.PasswordSalt,
                DisplayName = user.DisplayName,
                FullName = user.FullName,
                Department = user.Department,
                JobTitle = user.JobTitle,
                OperatorBadgeCode = user.OperatorBadgeCode,
                OperatorPinHash = user.OperatorPinHash,
                OperatorPinSalt = user.OperatorPinSalt,
                MustChangeOperatorPin = user.MustChangeOperatorPin,
                PhoneNumber = user.PhoneNumber,
                Email = user.Email,
                IsActive = user.IsActive,
                IsSuperAdmin = user.IsSuperAdmin,
                MustChangePassword = user.MustChangePassword,
                MfaEnabled = user.MfaEnabled,
                MfaSecretProtected = user.MfaSecretProtected,
                MfaRecoveryCodeHashesJson = user.MfaRecoveryCodeHashesJson,
                MfaEnrolledAtUtc = user.MfaEnrolledAtUtc,
                MfaLastVerifiedStep = user.MfaLastVerifiedStep,
                Role = user.Role,
                CanViewDashboard = user.CanViewDashboard,
                CanViewPermits = user.CanViewPermits,
                CanViewVisitorPermits = user.CanViewVisitorPermits,
                CanCreatePermit = user.CanCreatePermit,
                CanCreateVisitorPermit = user.CanCreateVisitorPermit,
                CanEditPermit = user.CanEditPermit,
                CanEditVisitorPermit = user.CanEditVisitorPermit,
                CanApprovePermit = user.CanApprovePermit,
                CanApproveLeaveRequest = user.CanApproveLeaveRequest,
                CanStopPermit = user.CanStopPermit,
                CanReviewUnauthorizedExit = user.CanReviewUnauthorizedExit,
                CanViewVisits = user.CanViewVisits,
                CanCreateVisit = user.CanCreateVisit,
                CanEditVisit = user.CanEditVisit,
                CanApproveDetainedVisit = user.CanApproveDetainedVisit,
                CanApproveVisits = user.CanApproveVisits,
                CanScanOperations = user.CanScanOperations,
                CanViewDisplays = user.CanViewDisplays,
                CanManageUsers = user.CanManageUsers,
                CanManageDepartments = user.CanManageDepartments,
                CanManageAdministration = user.CanManageAdministration,
                CanManageDelegations = user.CanManageDelegations,
                ManagerUsername = user.ManagerUsername,
            };
        }

        public static DisplayOperatorSessionInfo? CloneDisplayOperatorSession(
            DisplayOperatorSessionInfo? session
        )
        {
            if (session == null)
            {
                return null;
            }

            return new DisplayOperatorSessionInfo
            {
                Username = session.Username,
                DisplayName = session.DisplayName,
                BadgeCode = session.BadgeCode,
                DeviceId = session.DeviceId,
                SignedInAtUtc = session.SignedInAtUtc,
                ExpiresAtUtc = session.ExpiresAtUtc,
                MustChangePin = session.MustChangePin,
            };
        }
    }
}
