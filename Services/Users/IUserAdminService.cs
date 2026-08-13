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

namespace VehiclePermitSystemWeb.Services.Users
{
    public interface IUserAdminService
    {
        AdministrationSettings GetAdministrationSettings();
        AdministrationSettings GetAdministrationSettings(
            string tenantId,
            bool ignoreTenantFilters = false
        );
        void UpdateAdministrationSettings(AdministrationSettings settings);
        void InvalidateAdministrationSettingsCache();
        bool ValidateDisplayAccessKey(string? accessKey);
        IEnumerable<UserAccount> GetAllUsers(bool ignoreTenantFilters = false);
        IEnumerable<Tenant> GetTenants(bool includeInactive = false);
        IEnumerable<UserActivity> GetRecentUserActivities(int take = 20);
        IEnumerable<UserActivity> GetUserActivities(
            string? query = null,
            string? actionType = null,
            string? username = null,
            int take = 200
        );
        bool IsInitialSetupRequired();
        bool HasAnyUsers();
        UserAccount? GetUserAccount(string username, bool ignoreTenantFilters = false);
        bool CompleteInitialSetup(InitialSetupViewModel model);
        bool CreateUser(
            UserAccount user,
            string password,
            bool allowTenantSelection = false
        );
        bool UpdateUser(
            UserAccount user,
            string? newPassword = null,
            string? originalUsername = null,
            bool ignoreTenantFilters = false
        );
        string? SetUserActiveStatus(
            string username,
            bool isActive,
            bool ignoreTenantFilters = false
        );
        bool ChangePassword(string username, string currentPassword, string newPassword);
        string EnsureOperatorBadgeCode(
            string username,
            string? preferredBadgeCode = null,
            bool ignoreTenantFilters = false
        );
        string? ConfigureOperatorCredentials(
            string username,
            string badgeCode,
            string? temporaryPin,
            bool requirePinChange,
            bool ignoreTenantFilters = false
        );
        DisplayOperatorSessionInfo? GetDisplayOperatorSession(string deviceId);
        DisplayOperatorSwitchResult SwitchDisplayOperator(
            string deviceId,
            string badgeCode,
            string pin,
            string? recordedBy = null
        );
        bool SignOutDisplayOperator(
            string deviceId,
            out DisplayOperatorSessionInfo? previousOperator
        );
        bool ChangeDisplayOperatorPin(
            string deviceId,
            string currentPin,
            string newPin,
            out string errorCode
        );
        IEnumerable<Department> GetDepartments();
        IEnumerable<Department> GetDepartments(
            string tenantId,
            bool ignoreTenantFilters = false
        );
        IEnumerable<UserAccount> GetUsersByDepartment(string departmentName);
        Department? GetDepartment(int id);
        bool UpsertDepartment(Department department);
        bool SetDepartmentManager(int departmentId, string? managerUsername);
        string? HandoverDepartmentManager(
            DepartmentManagerHandoverRequest request,
            string? recordedBy = null
        );
        string? AssignGeneralManager(
            GeneralManagerAssignmentRequest request,
            string? recordedBy = null
        );
        string? GetDepartmentDeleteBlockReason(int id);
        bool DeleteDepartment(int id);
        bool IsUserDepartmentManager(string username);
        bool TransferDepartmentUsers(
            string sourceDepartmentName,
            string targetDepartmentName,
            IEnumerable<string> usernames,
            bool transferAll,
            string? recordedBy = null,
            string? replacementManagerUsername = null
        );
        bool ValidateCredentials(
            string username,
            string password,
            out string displayName,
            out string role
        );
        string CreateSession(string username);
        bool ValidateSession(string sessionId, out string? username);
        bool RefreshSession(string sessionId);
        void RemoveSession(string sessionId);
        void RecordUserActivity(
            string username,
            string displayName,
            string actionType,
            string actionLabel,
            string message,
            string source,
            string? recordedBy = null
        );
    }
}
