using System.Diagnostics.CodeAnalysis;
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

namespace VehiclePermitSystemWeb.Services.Administration
{
    public class AccessControlService : IAccessControlService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly IDelegationService _delegationService;

        public AccessControlService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            IDelegationService delegationService
        )
        {
            _dbFactory = dbFactory;
            _delegationService = delegationService;
        }

        public bool CanAccessPermit(Permit permit, UserAccount? user)
        {
            if (CanAccessPermitDirectly(permit, user))
            {
                return true;
            }

            if (permit == null || !CanUseAccessControl(user))
            {
                return false;
            }

            return _delegationService
                .ResolveExecutionContext(
                    user,
                    new[]
                    {
                        AppPermissions.ViewPermits,
                        AppPermissions.ViewVisitorPermits,
                        AppPermissions.EditPermit,
                        AppPermissions.EditVisitorPermit,
                        AppPermissions.ApprovePermit,
                        AppPermissions.StopPermit,
                        AppPermissions.ReviewUnauthorizedExit,
                    },
                    delegator =>
                        CanAccessPermitDirectly(permit, delegator)
                        || CanApprovePermitDirectly(permit, delegator)
                )
                .IsAllowed;
        }

        public bool CanApprovePermit(Permit permit, UserAccount? user)
        {
            if (CanApprovePermitDirectly(permit, user))
            {
                return true;
            }

            if (permit == null || !CanUseAccessControl(user))
            {
                return false;
            }

            return _delegationService
                .ResolveExecutionContext(
                    user,
                    new[] { AppPermissions.ApprovePermit },
                    delegator => CanApprovePermitDirectly(permit, delegator)
                )
                .IsAllowed;
        }

        public bool CanAccessVisit(Visit visit, UserAccount? user)
        {
            if (CanAccessVisitDirectly(visit, user))
            {
                return true;
            }

            if (visit == null || !CanUseAccessControl(user))
            {
                return false;
            }

            var visitPermissions = visit.IsDetainedVisit
                ? new[]
                {
                    AppPermissions.ViewVisits,
                    AppPermissions.ApproveVisits,
                    AppPermissions.ApproveDetainedVisit,
                }
                : new[] { AppPermissions.ViewVisits, AppPermissions.ApproveVisits };

            return _delegationService
                .ResolveExecutionContext(
                    user,
                    visitPermissions,
                    delegator =>
                        CanAccessVisitDirectly(visit, delegator)
                        || CanApproveVisitDirectly(visit, delegator)
                )
                .IsAllowed;
        }

        public bool CanViewPermits(UserAccount? user)
        {
            if (CanViewPermitsDirectly(user))
            {
                return true;
            }

            if (!CanUseAccessControl(user))
            {
                return false;
            }

            return _delegationService.HasEffectivePermission(user, AppPermissions.ViewPermits)
                || _delegationService.HasEffectivePermission(
                    user,
                    AppPermissions.ViewVisitorPermits
                )
                || _delegationService.HasEffectivePermission(user, AppPermissions.ApprovePermit)
                || _delegationService.HasEffectivePermission(user, AppPermissions.EditPermit)
                || _delegationService.HasEffectivePermission(user, AppPermissions.EditVisitorPermit)
                || _delegationService.HasEffectivePermission(user, AppPermissions.StopPermit)
                || _delegationService.HasEffectivePermission(
                    user,
                    AppPermissions.ReviewUnauthorizedExit
                );
        }

        public bool CanViewVisits(UserAccount? user)
        {
            if (CanViewVisitsDirectly(user))
            {
                return true;
            }

            if (!CanUseAccessControl(user))
            {
                return false;
            }

            return _delegationService.HasEffectivePermission(user, AppPermissions.ViewVisits)
                || _delegationService.HasEffectivePermission(user, AppPermissions.ApproveVisits)
                || _delegationService.HasEffectivePermission(
                    user,
                    AppPermissions.ApproveDetainedVisit
                );
        }

        public IEnumerable<Permit> FilterPermitsForUser(
            IEnumerable<Permit> permits,
            UserAccount? user
        )
        {
            return permits.Where(p => CanAccessPermit(p, user));
        }

        public IEnumerable<Visit> FilterVisitsForUser(IEnumerable<Visit> visits, UserAccount? user)
        {
            return visits.Where(v => CanAccessVisit(v, user));
        }

        public bool CanApproveVisit(Visit visit, UserAccount? user)
        {
            if (CanApproveVisitDirectly(visit, user))
            {
                return true;
            }

            if (visit == null || !CanUseAccessControl(user))
            {
                return false;
            }

            var visitPermissions = visit.IsDetainedVisit
                ? new[] { AppPermissions.ApproveVisits, AppPermissions.ApproveDetainedVisit }
                : new[] { AppPermissions.ApproveVisits };

            return _delegationService
                .ResolveExecutionContext(
                    user,
                    visitPermissions,
                    delegator => CanApproveVisitDirectly(visit, delegator)
                )
                .IsAllowed;
        }

        public bool IsCurrentDepartmentManager(UserAccount? user, string? departmentName = null)
        {
            return TryGetCurrentManagedDepartment(user, out var managedDepartment)
                && (
                    string.IsNullOrWhiteSpace(departmentName)
                    || string.Equals(
                        managedDepartment,
                        NormalizeComparisonValue(departmentName),
                        StringComparison.OrdinalIgnoreCase
                    )
                );
        }

        private static bool TryGetScopedDepartment(UserAccount? user, out string department)
        {
            department = string.Empty;
            if (user == null)
                return false;
            if (!user.IsActive)
                return false;
            if (!CanCurrentUserScopePermits(user))
                return false;
            department = NormalizeComparisonValue(user.Department);
            return !string.IsNullOrWhiteSpace(department);
        }

        private bool CanAccessPermitDirectly(Permit permit, UserAccount? user)
        {
            if (permit == null)
                return false;

            if (!CanUseAccessControl(user))
                return false;
            if (AppRoles.IsSuperAdmin(user))
            {
                return true;
            }
            if (HasGlobalOperationalAccess(user))
            {
                return true;
            }

            var hasPermitAccess =
                user.CanViewPermits
                || user.CanApprovePermit
                || user.CanEditPermit
                || user.CanStopPermit
                || user.CanReviewUnauthorizedExit;

            if (!hasPermitAccess)
            {
                return false;
            }

            if (
                !string.IsNullOrWhiteSpace(permit.CreatedBy)
                && string.Equals(
                    permit.CreatedBy,
                    user.Username,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (!TryGetScopedDepartment(user, out var dept))
            {
                return true;
            }

            return PermitMatchesDepartmentScope(permit, dept);
        }

        private bool CanApprovePermitDirectly(Permit permit, UserAccount? user)
        {
            if (permit == null)
                return false;
            if (user == null)
                return false;
            if (!user.IsActive)
                return false;
            if (AppRoles.IsSuperAdmin(user))
            {
                return true;
            }
            if (HasGlobalOperationalAccess(user))
            {
                return true;
            }

            if (!user.CanApprovePermit)
            {
                return false;
            }

            if (!TryGetCurrentManagedDepartment(user, out var dept))
            {
                return false;
            }

            return PermitMatchesDepartmentScope(permit, dept);
        }

        private bool CanAccessVisitDirectly(Visit visit, UserAccount? user)
        {
            if (visit == null)
                return false;
            if (!CanUseAccessControl(user))
                return false;
            if (AppRoles.IsSuperAdmin(user))
            {
                return true;
            }
            if (HasGlobalOperationalAccess(user))
            {
                return true;
            }

            if (user.CanViewVisits)
            {
                if (string.Equals(user.Role, AppRoles.Employee, StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        !string.IsNullOrWhiteSpace(visit.NationalId)
                        && string.Equals(
                            visit.NationalId,
                            user.Username,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return true;
                    }

                    if (
                        !string.IsNullOrWhiteSpace(visit.VisitedPersonName)
                        && string.Equals(
                            visit.VisitedPersonName,
                            user.FullName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return true;
                    }

                    return false;
                }

                return true;
            }

            return false;
        }

        private bool CanApproveVisitDirectly(Visit visit, UserAccount? user)
        {
            if (visit == null)
                return false;
            if (user == null)
                return false;
            if (!user.IsActive)
                return false;
            if (AppRoles.IsSuperAdmin(user))
            {
                return true;
            }
            if (HasGlobalOperationalAccess(user))
            {
                return true;
            }

            var canApproveCurrentVisit =
                user.CanApproveVisits || (visit.IsDetainedVisit && user.CanApproveDetainedVisit);

            if (!canApproveCurrentVisit)
            {
                return false;
            }

            if (!TryGetCurrentManagedDepartment(user, out var dept))
            {
                return false;
            }

            var normalized = NormalizeComparisonValue(visit.VisitLocation ?? visit.HostName);
            return normalized.Contains(dept, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanViewPermitsDirectly(UserAccount? user)
        {
            if (!CanUseAccessControl(user))
                return false;
            if (AppRoles.IsSuperAdmin(user))
            {
                return true;
            }
            if (HasGlobalOperationalAccess(user))
            {
                return true;
            }

            return user.CanViewPermits;
        }

        private static bool CanViewVisitsDirectly(UserAccount? user)
        {
            if (!CanUseAccessControl(user))
                return false;
            if (AppRoles.IsSuperAdmin(user))
            {
                return true;
            }
            if (HasGlobalOperationalAccess(user))
            {
                return true;
            }

            return user.CanViewVisits || user.CanApproveVisits || user.CanApproveDetainedVisit;
        }

        private bool TryGetCurrentManagedDepartment(UserAccount? user, out string department)
        {
            department = string.Empty;
            if (user == null || !user.IsActive)
            {
                return false;
            }

            if (
                !string.Equals(
                    user.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            var scopedDepartment = NormalizeComparisonValue(user.Department);
            if (string.IsNullOrWhiteSpace(scopedDepartment))
            {
                return false;
            }

            using var db = _dbFactory.CreateDbContext();
            var managedDepartments = db
                .Departments.AsNoTracking()
                .Where(dept => dept.IsActive && !string.IsNullOrWhiteSpace(dept.ManagerUsername))
                .AsEnumerable()
                .Where(dept =>
                    string.Equals(
                        dept.ManagerUsername,
                        user.Username,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Select(dept => NormalizeComparisonValue(dept.Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToList();

            if (managedDepartments.Count == 0)
            {
                return false;
            }

            var matchedDepartment = managedDepartments.FirstOrDefault(name =>
                string.Equals(name, scopedDepartment, StringComparison.OrdinalIgnoreCase)
            );

            if (string.IsNullOrWhiteSpace(matchedDepartment))
            {
                return false;
            }

            department = matchedDepartment;
            return true;
        }

        private static bool CanCurrentUserScopePermits(UserAccount? user)
        {
            if (user == null)
                return false;
            if (!user.IsActive)
                return false;
            if (AppRoles.IsSuperAdmin(user))
                return false;
            return string.Equals(user.Role, AppRoles.Manager, StringComparison.OrdinalIgnoreCase)
                || string.Equals(user.Role, AppRoles.Employee, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasGlobalOperationalAccess(UserAccount user)
        {
            return string.Equals(
                    user.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    user.Role,
                    AppRoles.SecurityManager,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool PermitMatchesDepartmentScope(Permit permit, string department)
        {
            if (string.IsNullOrWhiteSpace(department))
                return true;
            if (permit == null)
                return false;

            var deptVal = NormalizeComparisonValue(department);
            if (
                !string.IsNullOrWhiteSpace(permit.DepartmentName)
                && NormalizeComparisonValue(permit.DepartmentName)
                    .Contains(deptVal, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            if (
                !string.IsNullOrWhiteSpace(permit.EmployeeDepartment)
                && NormalizeComparisonValue(permit.EmployeeDepartment)
                    .Contains(deptVal, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            return false;
        }

        private static string NormalizeComparisonValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var normalized = value.Trim();
            normalized = string.Join(
                " ",
                normalized.Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            );
            return NormalizeArabicDigits(normalized).ToUpperInvariant();
        }

        private static bool CanUseAccessControl([NotNullWhen(true)] UserAccount? user)
        {
            return user != null && user.IsActive;
        }

        private static string NormalizeArabicDigits(string value)
        {
            return value
                .Replace('٠', '0')
                .Replace('١', '1')
                .Replace('٢', '2')
                .Replace('٣', '3')
                .Replace('٤', '4')
                .Replace('٥', '5')
                .Replace('٦', '6')
                .Replace('٧', '7')
                .Replace('٨', '8')
                .Replace('٩', '9');
        }
    }
}
