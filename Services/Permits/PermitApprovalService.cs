using System.Text.Json;
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

namespace VehiclePermitSystemWeb.Services.Permits
{
    public sealed class PermitApprovalService : IPermitApprovalService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;
        private readonly IPermitAuditService _permitAuditService;
        private readonly IAccessControlService _accessControlService;
        private readonly IDelegationService _delegationService;

        public PermitApprovalService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock,
            IPermitAuditService permitAuditService,
            IAccessControlService accessControlService,
            IDelegationService delegationService
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
            _permitAuditService = permitAuditService;
            _accessControlService = accessControlService;
            _delegationService = delegationService;
        }

        public void UpdatePermitApprovalStatus(
            string permitNumber,
            string approvalStatus,
            string? performedBy = null,
            bool? requiresReturn = null,
            DateTime? expectedReturnTime = null,
            bool? pendingExitRequest = null,
            string? leaveReason = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            ExecuteInTransaction(
                db,
                () =>
                {
                    var permit = db.Permits.FirstOrDefault(p => p.PermitNumber == permitNumber);
                    if (permit == null)
                    {
                        return 0;
                    }

                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        var approver = db.UserAccounts.FirstOrDefault(u =>
                            u.Username == performedBy
                        );
                        var approvalContext = _delegationService.ResolveExecutionContext(
                            approver,
                            new[] { AppPermissions.ApprovePermit },
                            delegator => CanApprovePermitDirectly(db, permit, delegator)
                        );
                        if (!approvalContext.IsAllowed)
                        {
                            return 0;
                        }

                        var delegatedSuffix = approvalContext.IsDelegated
                            ? $" بتفويض من {approvalContext.Delegator?.DisplayName ?? approvalContext.Delegator?.Username}"
                            : string.Empty;

                        permit.ApprovalStatus = approvalStatus;
                        // When approving, allow manager to set return/leave options
                        if (
                            string.Equals(
                                approvalStatus,
                                "Approved",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            if (requiresReturn.HasValue)
                            {
                                if (permit.IsPermanentPermit)
                                {
                                    permit.AccessMode = requiresReturn.Value
                                        ? Permit.AccessModeFullAccess
                                        : Permit.AccessModeEntryOnly;
                                }

                                permit.RequiresReturn = requiresReturn.Value;
                                permit.NormalizeAccessModeState();
                            }

                            if (expectedReturnTime.HasValue)
                            {
                                permit.ExpectedReturnTime = expectedReturnTime.Value;
                            }

                            if (pendingExitRequest.HasValue)
                            {
                                permit.PendingExitRequest = pendingExitRequest.Value;
                            }

                            if (!string.IsNullOrWhiteSpace(leaveReason))
                            {
                                permit.LeaveReason = leaveReason.Trim();
                            }
                        }
                        if (
                            !string.Equals(
                                approvalStatus,
                                "Approved",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            ResetExitRequestState(permit);
                        }

                        var isApproved = string.Equals(
                            approvalStatus,
                            "Approved",
                            StringComparison.OrdinalIgnoreCase
                        );
                        _permitAuditService.RecordUserActivity(
                            db,
                            permit.PermitNumber,
                            permit.DriverName,
                            isApproved ? "Approve" : "Reject",
                            isApproved ? "اعتماد تصريح" : "رفض تصريح",
                            isApproved
                                ? $"تم اعتماد التصريح رقم {permit.PermitNumber} الخاص بـ {permit.DriverName}{delegatedSuffix}."
                                : $"تم رفض التصريح رقم {permit.PermitNumber} الخاص بـ {permit.DriverName}{delegatedSuffix}.",
                            "PermitsController",
                            performedBy,
                            _systemClock.LocalNow,
                            performedBy,
                            approvalContext.IsDelegated,
                            approvalContext.Delegator?.Username,
                            approvalContext.Delegation?.Id
                        );

                        if (approvalContext.IsDelegated)
                        {
                            db.AuditLogs.Add(
                                new AuditLog
                                {
                                    Username = performedBy,
                                    ActualActorUsername = performedBy,
                                    ActionType = "DelegatedApproval",
                                    ActionLabel = isApproved ? "اعتماد بتفويض" : "رفض بتفويض",
                                    EntityType = "Permit",
                                    EntityId = permit.PermitNumber,
                                    Message = isApproved
                                        ? $"تم اعتماد التصريح بواسطة {performedBy} بتفويض من {approvalContext.Delegator?.Username}."
                                        : $"تم رفض التصريح بواسطة {performedBy} بتفويض من {approvalContext.Delegator?.Username}.",
                                    Source = nameof(PermitApprovalService),
                                    RecordedBy = performedBy,
                                    OccurredAt = _systemClock.UtcNow,
                                    Success = true,
                                    ActedUnderDelegation = true,
                                    DelegatedFromUsername =
                                        approvalContext.Delegator?.Username ?? string.Empty,
                                    DelegationId = approvalContext.Delegation?.Id,
                                    AfterJson = JsonSerializer.Serialize(
                                        new
                                        {
                                            permit.PermitNumber,
                                            permit.DriverName,
                                            approvalStatus,
                                            ActualActorUsername = performedBy,
                                            DelegatedFromUsername = approvalContext
                                                .Delegator
                                                ?.Username,
                                            approvalContext.Delegation?.Id,
                                        }
                                    ),
                                }
                            );
                        }

                        db.SaveChanges();
                        return 0;
                    }

                    permit.ApprovalStatus = approvalStatus;
                    if (
                        string.Equals(
                            approvalStatus,
                            "Approved",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        if (requiresReturn.HasValue)
                        {
                            if (permit.IsPermanentPermit)
                            {
                                permit.AccessMode = requiresReturn.Value
                                    ? Permit.AccessModeFullAccess
                                    : Permit.AccessModeEntryOnly;
                            }

                            permit.RequiresReturn = requiresReturn.Value;
                            permit.NormalizeAccessModeState();
                        }

                        if (expectedReturnTime.HasValue)
                        {
                            permit.ExpectedReturnTime = expectedReturnTime.Value;
                        }

                        if (pendingExitRequest.HasValue)
                        {
                            permit.PendingExitRequest = pendingExitRequest.Value;
                        }

                        if (!string.IsNullOrWhiteSpace(leaveReason))
                        {
                            permit.LeaveReason = leaveReason.Trim();
                        }
                    }
                    if (
                        !string.Equals(
                            approvalStatus,
                            "Approved",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        ResetExitRequestState(permit);
                    }

                    db.SaveChanges();
                    return 0;
                }
            );
        }

        public bool ForwardPermitToGeneralManager(string permitNumber, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    var permit = db.Permits.FirstOrDefault(p => p.PermitNumber == permitNumber);
                    if (
                        permit == null
                        || permit.ArchivedAt.HasValue
                        || !string.Equals(
                            permit.ApprovalStatus,
                            Permit.ApprovalStatusPendingReview,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return false;
                    }

                    var securityManager = db
                        .UserAccounts.AsNoTracking()
                        .Where(user =>
                            user.IsActive
                            && user.Role == AppRoles.SecurityManager
                            && user.CanApprovePermit
                        )
                        .OrderBy(user => user.DisplayName)
                        .FirstOrDefault();

                    var configuredGeneralManagerUsername = db
                        .AdministrationSettings.AsNoTracking()
                        .Where(settings => settings.Id == 1)
                        .Select(settings => settings.GeneralManagerUsername)
                        .FirstOrDefault();

                    var generalManager = securityManager ?? (!string.IsNullOrWhiteSpace(
                        configuredGeneralManagerUsername
                    )
                        ? db
                            .UserAccounts.AsNoTracking()
                            .FirstOrDefault(user =>
                                user.Username == configuredGeneralManagerUsername
                                && user.IsActive
                                && user.Role == AppRoles.GeneralManager
                                && user.CanApprovePermit
                            )
                        : null);

                    generalManager ??= db
                        .UserAccounts.AsNoTracking()
                        .Where(user =>
                            user.IsActive
                            && user.Role == AppRoles.GeneralManager
                            && user.CanApprovePermit
                        )
                        .OrderBy(user => user.DisplayName)
                        .FirstOrDefault();

                    // Keep initial setup operational until a dedicated security manager exists.
                    generalManager ??= db
                        .UserAccounts.AsNoTracking()
                        .Where(user => user.IsActive && user.IsSuperAdmin)
                        .OrderBy(user => user.DisplayName)
                        .FirstOrDefault();

                    if (generalManager == null)
                    {
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        var forwardedBy = db.UserAccounts.FirstOrDefault(user =>
                            user.Username == performedBy
                        );
                        if (
                            forwardedBy == null
                            || !forwardedBy.IsActive
                            || !(
                                AppRoles.IsSuperAdmin(forwardedBy)
                                || (
                                    forwardedBy.Role == AppRoles.PermitReviewer
                                    && forwardedBy.CanEditPermit
                                )
                            )
                        )
                        {
                            return false;
                        }
                    }

                    permit.ManagerName = generalManager.DisplayName;
                    permit.ApprovalStatus = Permit.ApprovalStatusPending;

                    _permitAuditService.RecordPermitActivity(
                        db,
                        permit,
                        "SubmitForSecurityApproval",
                        "اكتمال التدقيق",
                        $"اكتمل تدقيق الطلب رقم {permit.PermitNumber} وتم رفعه إلى مدير الأمن.",
                        "PermitsController",
                        performedBy,
                        _systemClock.LocalNow,
                        reasonCode: "SubmitForSecurityApproval"
                    );
                    _permitAuditService.RecordUserActivity(
                        db,
                        generalManager.Username,
                        generalManager.DisplayName,
                        "SecurityApprovalRequested",
                        "طلب بانتظار اعتماد الأمن",
                        $"اكتمل تدقيق الطلب رقم {permit.PermitNumber} وهو بانتظار اعتماد مدير الأمن.",
                        "PermitsController",
                        performedBy,
                        _systemClock.LocalNow
                    );

                    db.SaveChanges();
                    return true;
                }
            );
        }

        private static void ResetExitRequestState(Permit permit)
        {
            permit.TemporaryExitPermissionUntil = null;
            permit.PendingExitRequest = false;
            permit.LeaveReason = string.Empty;
            permit.ExpectedReturnTime = null;
            permit.LeaveWindowStartAt = null;
            permit.LeaveWindowEndAt = null;
        }

        private static bool CanApprovePermitDirectly(
            ApplicationDbContext db,
            Permit permit,
            UserAccount? user
        )
        {
            if (permit == null || user == null || !user.IsActive)
            {
                return false;
            }

            if (AppRoles.IsSuperAdmin(user))
            {
                return true;
            }

            if (
                string.Equals(
                    user.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    user.Role,
                    AppRoles.SecurityManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (!user.CanApprovePermit)
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

            var scopedDepartment = NormalizeValue(user.Department);
            if (string.IsNullOrWhiteSpace(scopedDepartment))
            {
                return false;
            }

            var managedDepartments = db
                .Departments.AsNoTracking()
                .Where(department =>
                    department.IsActive && department.ManagerUsername == user.Username
                )
                .Select(department => department.Name)
                .AsEnumerable()
                .Select(NormalizeValue)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!managedDepartments.Contains(scopedDepartment))
            {
                return false;
            }

            var permitDepartments = new[] { permit.DepartmentName, permit.EmployeeDepartment }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeValue)
                .ToList();
            return permitDepartments.Any(value =>
                value.Contains(scopedDepartment, StringComparison.OrdinalIgnoreCase)
            );
        }

        private static string NormalizeValue(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static T ExecuteInTransaction<T>(ApplicationDbContext db, Func<T> action)
        {
            using var transaction = db.Database.BeginTransaction();
            try
            {
                var result = action();
                transaction.Commit();
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
