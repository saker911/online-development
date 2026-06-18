using System.Reflection;
using System.Security.Claims;
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

namespace VehiclePermitSystemWeb.Security
{
    public static class AppClaimTypes
    {
        public const string SuperAdmin = "super_admin";
        public const string TenantId = "tenant_id";
    }

    public static class DisplayAccessDefaults
    {
        public static string CreateAccessKey()
        {
            var configuredKey = Environment.GetEnvironmentVariable(
                "VehiclePermitSystemWeb__Security__DisplayAccessKey"
            );

            return string.IsNullOrWhiteSpace(configuredKey)
                ? SecureTokenGenerator.GenerateSecureToken()
                : configuredKey.Trim();
        }

        public static bool LooksLikePlaceholder(string? accessKey)
        {
            var normalized = (accessKey ?? string.Empty).Trim();
            return normalized.Length < 32
                || normalized.Contains("change_this", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("secure", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("2026", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class AppRoles
    {
        public const string GeneralManager = "GeneralManager";

        // New canonical roles
        public const string Employee = "Employee";
        public const string Manager = "Manager";
        public const string GateSecurity = "GateSecurity";
        public const string Receptionist = "Receptionist";
        public const string SystemAdmin = "SystemAdmin";

        // Backwards-compatible alias
        public const string DepartmentManager = Manager;

        private static readonly IReadOnlyList<string> _orderedRoles =
        [
            SystemAdmin,
            GeneralManager,
            Manager,
            Employee,
            GateSecurity,
            Receptionist,
        ];

        public static IReadOnlyList<string> OrderedRoles => _orderedRoles;

        public static string GetDisplayName(string? role, bool isSuperAdmin = false) =>
            isSuperAdmin
                ? "مالك النظام"
                : role switch
                {
                    GeneralManager => "مدير عام",
                    Manager or DepartmentManager => "مدير",
                    Employee => "موظف",
                    SystemAdmin => "مشرف نظام",
                    GateSecurity => "أمن البوابة",
                    Receptionist => "موظف استقبال",
                    _ => "مستخدم النظام",
                };

        public static int GetSortOrder(string? role, bool isSuperAdmin = false)
        {
            if (isSuperAdmin)
            {
                return -1;
            }

            for (var index = 0; index < _orderedRoles.Count; index++)
            {
                if (string.Equals(_orderedRoles[index], role, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        public static string GetDisplayName(UserAccount? user) =>
            GetDisplayName(user?.Role, user?.IsSuperAdmin == true);

        public static bool IsSuperAdmin(UserAccount? user) => user?.IsSuperAdmin == true;

        public static string GetOwnershipCaption(UserAccount? user) =>
            IsSuperAdmin(user) ? "مالك النظام" : string.Empty;

        public static string GetOwnershipCaption(ClaimsPrincipal principal) =>
            principal.IsSuperAdmin() ? "مالك النظام" : string.Empty;
    }

    public static class AppPermissions
    {
        public const string ClaimType = "permission";

        public const string ViewDashboard = nameof(ViewDashboard);
        public const string ViewPermits = nameof(ViewPermits);
        public const string ViewVisitorPermits = nameof(ViewVisitorPermits);
        public const string CreatePermit = nameof(CreatePermit);
        public const string CreateVisitorPermit = nameof(CreateVisitorPermit);
        public const string EditPermit = nameof(EditPermit);
        public const string EditVisitorPermit = nameof(EditVisitorPermit);
        public const string ApprovePermit = nameof(ApprovePermit);
        public const string ApproveLeaveRequest = nameof(ApproveLeaveRequest);
        public const string StopPermit = nameof(StopPermit);
        public const string ReviewUnauthorizedExit = nameof(ReviewUnauthorizedExit);
        public const string ViewVisits = nameof(ViewVisits);
        public const string CreateVisit = nameof(CreateVisit);
        public const string EditVisit = nameof(EditVisit);
        public const string ApproveDetainedVisit = nameof(ApproveDetainedVisit);
        public const string ApproveVisits = nameof(ApproveVisits);
        public const string ViewDisplays = nameof(ViewDisplays);
        public const string ScanOperations = nameof(ScanOperations);
        public const string ManageUsers = nameof(ManageUsers);
        public const string ManageDepartments = nameof(ManageDepartments);
        public const string ManageAdministration = nameof(ManageAdministration);
        public const string ManageDelegations = nameof(ManageDelegations);

        public sealed record PermissionEditorItem(
            string Key,
            string UserFlagPropertyName,
            string ItemLabel,
            string DisplayName,
            string? InputId = null
        );

        public sealed record PermissionEditorGroup(
            string Key,
            string Title,
            string Description,
            string PrimaryIcon,
            string SecondaryIcon,
            IReadOnlyList<PermissionEditorItem> Permissions
        );

        private static readonly IReadOnlyList<PermissionEditorGroup> _editorGroups =
            BuildEditorGroups();
        private static readonly IReadOnlyDictionary<
            string,
            PermissionEditorItem
        > _editorItemsByKey = _editorGroups
            .SelectMany(group => group.Permissions)
            .ToDictionary(item => item.Key, StringComparer.Ordinal);
        private static readonly IReadOnlyDictionary<
            string,
            PropertyInfo
        > _userPermissionProperties = typeof(UserAccount)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(bool))
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        public static IReadOnlyList<PermissionEditorGroup> EditorGroups => _editorGroups;

        public static int TotalPermissionCount => _editorItemsByKey.Count;

        public static void ApplyRoleDefaults(UserAccount user)
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

            switch (user.Role)
            {
                case AppRoles.GeneralManager:
                    user.CanViewDashboard = true;
                    user.CanViewPermits = true;
                    user.CanViewVisitorPermits = true;
                    user.CanCreatePermit = true;
                    user.CanCreateVisitorPermit = true;
                    user.CanEditPermit = true;
                    user.CanEditVisitorPermit = true;
                    user.CanApprovePermit = true;
                    user.CanApproveLeaveRequest = true;
                    user.CanStopPermit = true;
                    user.CanReviewUnauthorizedExit = true;
                    user.CanViewVisits = true;
                    user.CanCreateVisit = true;
                    user.CanEditVisit = true;
                    user.CanApproveDetainedVisit = true;
                    user.CanScanOperations = true;
                    user.CanViewDisplays = true;
                    user.CanManageUsers = true;
                    user.CanManageDepartments = true;
                    user.CanManageAdministration = true;
                    user.CanManageDelegations = true;
                    user.CanApproveVisits = true;
                    break;
                case AppRoles.Manager:
                    user.CanViewDashboard = true;
                    user.CanViewPermits = true;
                    user.CanViewVisitorPermits = true;
                    user.CanCreatePermit = true;
                    user.CanCreateVisitorPermit = true;
                    user.CanEditPermit = true;
                    user.CanEditVisitorPermit = true;
                    user.CanApprovePermit = true;
                    user.CanApproveLeaveRequest = true;
                    user.CanViewVisits = true;
                    user.CanCreateVisit = true;
                    user.CanEditVisit = true;
                    user.CanApproveDetainedVisit = true;
                    user.CanApproveVisits = true;
                    user.CanStopPermit = true;
                    user.CanReviewUnauthorizedExit = true;
                    user.CanScanOperations = false;
                    user.CanViewDisplays = false;
                    user.CanManageUsers = false;
                    user.CanManageDepartments = false;
                    user.CanManageAdministration = false;
                    break;
                case AppRoles.Employee:
                    user.CanViewDashboard = true;
                    user.CanViewVisits = true;
                    break;
                case AppRoles.SystemAdmin:
                    user.CanViewDashboard = true;
                    user.CanManageUsers = true;
                    user.CanManageDepartments = true;
                    user.CanManageAdministration = true;
                    user.CanManageDelegations = true;
                    user.CanViewDisplays = true;
                    break;
                case AppRoles.GateSecurity:
                    user.CanViewDashboard = true;
                    user.CanViewPermits = true;
                    user.CanViewVisitorPermits = true;
                    user.CanViewVisits = true;
                    user.CanScanOperations = true;
                    user.CanViewDisplays = true;
                    break;
                case AppRoles.Receptionist:
                    user.CanViewDashboard = true;
                    user.CanViewVisitorPermits = true;
                    user.CanCreateVisitorPermit = true;
                    user.CanEditVisitorPermit = true;
                    user.CanViewVisits = true;
                    user.CanCreateVisit = true;
                    user.CanEditVisit = true;
                    user.CanViewDisplays = true;
                    break;
            }

            if (user.IsSuperAdmin)
            {
                user.CanViewDashboard = true;
                user.CanViewPermits = true;
                user.CanViewVisitorPermits = true;
                user.CanCreatePermit = true;
                user.CanCreateVisitorPermit = true;
                user.CanEditPermit = true;
                user.CanEditVisitorPermit = true;
                user.CanApprovePermit = true;
                user.CanApproveLeaveRequest = true;
                user.CanStopPermit = true;
                user.CanReviewUnauthorizedExit = true;
                user.CanViewVisits = true;
                user.CanCreateVisit = true;
                user.CanEditVisit = true;
                user.CanApproveDetainedVisit = true;
                user.CanApproveVisits = true;
                user.CanScanOperations = true;
                user.CanViewDisplays = true;
                user.CanManageUsers = true;
                user.CanManageDepartments = true;
                user.CanManageAdministration = true;
                user.CanManageDelegations = true;
            }
        }

        public static IEnumerable<string> GetGrantedPermissions(UserAccount user)
        {
            foreach (var item in _editorGroups.SelectMany(group => group.Permissions))
            {
                if (HasUserPermissionFlag(user, item.UserFlagPropertyName))
                {
                    yield return item.Key;
                }
            }
        }

        public static string GetDisplayName(string permission) =>
            _editorItemsByKey.TryGetValue(permission, out var item) ? item.DisplayName : permission;

        private static bool HasUserPermissionFlag(UserAccount user, string propertyName)
        {
            return _userPermissionProperties.TryGetValue(propertyName, out var property)
                && property.GetValue(user) is true;
        }

        private static IReadOnlyList<PermissionEditorGroup> BuildEditorGroups()
        {
            return new List<PermissionEditorGroup>
            {
                new(
                    "employee-permits",
                    "تصاريح الموظفين",
                    "عرض وإنشاء وتعديل واعتماد التصاريح وما يتبعها من استئذان ومراجعة",
                    "📄",
                    "🧾",
                    new List<PermissionEditorItem>
                    {
                        new(
                            ViewPermits,
                            nameof(UserAccount.CanViewPermits),
                            "عرض",
                            "تصاريح الموظفين: عرض"
                        ),
                        new(
                            CreatePermit,
                            nameof(UserAccount.CanCreatePermit),
                            "إنشاء",
                            "تصاريح الموظفين: إنشاء"
                        ),
                        new(
                            EditPermit,
                            nameof(UserAccount.CanEditPermit),
                            "تعديل",
                            "تصاريح الموظفين: تعديل"
                        ),
                        new(
                            ApprovePermit,
                            nameof(UserAccount.CanApprovePermit),
                            "اعتماد",
                            "تصاريح الموظفين: اعتماد"
                        ),
                        new(
                            ApproveLeaveRequest,
                            nameof(UserAccount.CanApproveLeaveRequest),
                            "اعتماد الاستئذان",
                            "تصاريح الموظفين: اعتماد الاستئذان"
                        ),
                        new(
                            StopPermit,
                            nameof(UserAccount.CanStopPermit),
                            "إيقاف/إعادة تفعيل",
                            "تصاريح الموظفين: إيقاف وإعادة تفعيل"
                        ),
                        new(
                            ReviewUnauthorizedExit,
                            nameof(UserAccount.CanReviewUnauthorizedExit),
                            "مراجعة الخروج غير المحسوم",
                            "تصاريح الموظفين: مراجعة الخروج غير المحسوم"
                        ),
                    }
                ),
                new(
                    "visitor-permits",
                    "تصاريح الزوار",
                    "صلاحيات التصاريح المؤقتة الخاصة بالزوار منفصلة عن سجل الزيارات نفسه",
                    "🪪",
                    "👤",
                    new List<PermissionEditorItem>
                    {
                        new(
                            ViewVisitorPermits,
                            nameof(UserAccount.CanViewVisitorPermits),
                            "عرض",
                            "تصاريح الزوار: عرض"
                        ),
                        new(
                            CreateVisitorPermit,
                            nameof(UserAccount.CanCreateVisitorPermit),
                            "إنشاء",
                            "تصاريح الزوار: إنشاء"
                        ),
                        new(
                            EditVisitorPermit,
                            nameof(UserAccount.CanEditVisitorPermit),
                            "تعديل",
                            "تصاريح الزوار: تعديل"
                        ),
                    }
                ),
                new(
                    "visits",
                    "الزيارات",
                    "إدارة سجل الزيارات واعتمادها، بما في ذلك الزيارات الموقوفة كمسار متخصص",
                    "👥",
                    "🏢",
                    new List<PermissionEditorItem>
                    {
                        new(ViewVisits, nameof(UserAccount.CanViewVisits), "عرض", "الزيارات: عرض"),
                        new(
                            CreateVisit,
                            nameof(UserAccount.CanCreateVisit),
                            "إنشاء",
                            "الزيارات: إنشاء"
                        ),
                        new(
                            EditVisit,
                            nameof(UserAccount.CanEditVisit),
                            "تعديل",
                            "الزيارات: تعديل"
                        ),
                        new(
                            ApproveVisits,
                            nameof(UserAccount.CanApproveVisits),
                            "اعتماد",
                            "الزيارات: اعتماد"
                        ),
                        new(
                            ApproveDetainedVisit,
                            nameof(UserAccount.CanApproveDetainedVisit),
                            "اعتماد الزيارات الموقوفة",
                            "الزيارات: اعتماد الزيارات الموقوفة"
                        ),
                    }
                ),
                new(
                    "operations",
                    "التشغيل والمتابعة",
                    "الوصول إلى اللوحة الرئيسية، شاشات العرض، وعمليات المسح على البوابة",
                    "📊",
                    "🚪",
                    new List<PermissionEditorItem>
                    {
                        new(
                            ViewDashboard,
                            nameof(UserAccount.CanViewDashboard),
                            "عرض اللوحة الرئيسية",
                            "التشغيل والمتابعة: عرض اللوحة الرئيسية"
                        ),
                        new(
                            ViewDisplays,
                            nameof(UserAccount.CanViewDisplays),
                            "عرض الشاشات",
                            "التشغيل والمتابعة: عرض الشاشات"
                        ),
                        new(
                            ScanOperations,
                            nameof(UserAccount.CanScanOperations),
                            "مسح الباركود",
                            "التشغيل والمتابعة: مسح الباركود",
                            "users-scan-toggle"
                        ),
                    }
                ),
                new(
                    "administration",
                    "الإدارة والنظام",
                    "إدارة المستخدمين والأقسام وإعدادات الإدارة دون المساس بتخزين الصلاحيات الحالي",
                    "⚙️",
                    "🛡️",
                    new List<PermissionEditorItem>
                    {
                        new(
                            ManageUsers,
                            nameof(UserAccount.CanManageUsers),
                            "إدارة المستخدمين والصلاحيات",
                            "الإدارة والنظام: إدارة المستخدمين والصلاحيات",
                            "users-manage-users-toggle"
                        ),
                        new(
                            ManageDepartments,
                            nameof(UserAccount.CanManageDepartments),
                            "إدارة الأقسام",
                            "الإدارة والنظام: إدارة الأقسام",
                            "users-manage-departments-toggle"
                        ),
                        new(
                            ManageAdministration,
                            nameof(UserAccount.CanManageAdministration),
                            "إدارة بيانات الإدارة",
                            "الإدارة والنظام: إدارة بيانات الإدارة",
                            "users-manage-administration-toggle"
                        ),
                        new(
                            ManageDelegations,
                            nameof(UserAccount.CanManageDelegations),
                            "إدارة التفويضات المؤقتة",
                            "الإدارة والنظام: إدارة التفويضات المؤقتة",
                            "users-manage-delegations-toggle"
                        ),
                    }
                ),
            };
        }
    }

    public static class ClaimsPrincipalPermissionsExtensions
    {
        public static bool IsSuperAdmin(this ClaimsPrincipal principal)
        {
            return principal.HasClaim(AppClaimTypes.SuperAdmin, "true");
        }

        public static bool HasPermission(this ClaimsPrincipal principal, string permission)
        {
            if (principal.IsSuperAdmin())
            {
                return true;
            }

            if (principal.HasClaim(AppPermissions.ClaimType, permission))
            {
                return true;
            }

            if (
                string.Equals(
                    permission,
                    AppPermissions.ApproveDetainedVisit,
                    StringComparison.Ordinal
                )
            )
            {
                return principal.HasClaim(AppPermissions.ClaimType, AppPermissions.ApproveVisits);
            }

            if (
                string.Equals(
                    permission,
                    AppPermissions.ReviewUnauthorizedExit,
                    StringComparison.Ordinal
                )
            )
            {
                return principal.IsInRole(AppRoles.GeneralManager)
                    || principal.IsInRole(AppRoles.DepartmentManager);
            }

            return string.Equals(permission, AppPermissions.StopPermit, StringComparison.Ordinal)
                && (
                    principal.IsInRole(AppRoles.GeneralManager)
                    || principal.IsInRole(AppRoles.DepartmentManager)
                );
        }
    }

    public static class AppPolicies
    {
        public const string ViewDashboard = nameof(ViewDashboard);
        public const string ViewPermits = nameof(ViewPermits);
        public const string CreatePermits = nameof(CreatePermits);
        public const string ViewVisitorPermits = nameof(ViewVisitorPermits);
        public const string CreateVisitorPermits = nameof(CreateVisitorPermits);
        public const string EditPermits = nameof(EditPermits);
        public const string EditVisitorPermits = nameof(EditVisitorPermits);
        public const string ApprovePermits = nameof(ApprovePermits);
        public const string ApproveLeaveRequests = nameof(ApproveLeaveRequests);
        public const string StopPermits = nameof(StopPermits);
        public const string ReviewUnauthorizedExits = nameof(ReviewUnauthorizedExits);
        public const string ViewVisits = nameof(ViewVisits);
        public const string CreateVisits = nameof(CreateVisits);
        public const string EditVisits = nameof(EditVisits);
        public const string ApproveDetainedVisits = nameof(ApproveDetainedVisits);
        public const string ViewDisplays = nameof(ViewDisplays);
        public const string ScanOperations = nameof(ScanOperations);
        public const string ManageUsers = nameof(ManageUsers);
        public const string ManageDepartments = nameof(ManageDepartments);
        public const string ManageAdministration = nameof(ManageAdministration);
        public const string ManageDelegations = nameof(ManageDelegations);
    }
}
