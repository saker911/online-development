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

namespace VehiclePermitSystemWeb.Services.Audit
{
    public sealed class PermitAuditService : IPermitAuditService
    {
        public void RecordPermitActivity(
            ApplicationDbContext db,
            Permit permit,
            string actionType,
            string actionLabel,
            string message,
            string source,
            string? recordedBy,
            DateTime occurredAt,
            string? reasonCode = null,
            int? lateMinutes = null,
            PermitScanAuditContext? auditContext = null
        )
        {
            db.PermitActivities.Add(
                new PermitActivity
                {
                    PermitNumber = permit.PermitNumber,
                    DriverName = permit.DriverName,
                    NationalId = string.Empty,
                    DepartmentName = permit.DepartmentName,
                    ActionType = actionType,
                    ActionLabel = actionLabel,
                    Message = message,
                    Source = source,
                    RecordedBy = recordedBy ?? string.Empty,
                    ReasonCode = reasonCode ?? string.Empty,
                    SequenceId = auditContext?.SequenceId ?? string.Empty,
                    ClassificationStatus = auditContext?.ClassificationStatus ?? string.Empty,
                    GateName = auditContext?.GateName ?? string.Empty,
                    GateOperatorName = auditContext?.GateOperatorName ?? string.Empty,
                    GateOperatorAccount = auditContext?.GateOperatorAccount ?? string.Empty,
                    DeviceId = auditContext?.DeviceId ?? string.Empty,
                    IpAddress = auditContext?.IpAddress ?? string.Empty,
                    ExecutionMethod = auditContext?.ExecutionMethod ?? string.Empty,
                    IsAutomated = auditContext?.IsAutomated ?? false,
                    OccurredAt = occurredAt,
                    LateMinutes = lateMinutes,
                }
            );

            // Add central audit log entry for this permit activity
            try
            {
                db.AuditLogs.Add(
                    new AuditLog
                    {
                        Username = recordedBy ?? string.Empty,
                        ActionType = actionType,
                        ActionLabel = actionLabel,
                        EntityType = "Permit",
                        EntityId = permit.PermitNumber,
                        Message = message,
                        Source = source,
                        RecordedBy = recordedBy ?? string.Empty,
                        OccurredAt = occurredAt,
                        IpAddress = auditContext?.IpAddress ?? string.Empty,
                        Success = !string.Equals(
                            actionType,
                            "PendingUnauthorizedExit",
                            StringComparison.OrdinalIgnoreCase
                        ),
                        AfterJson = JsonSerializer.Serialize(
                            new
                            {
                                permit.PermitNumber,
                                permit.DriverName,
                                permit.ApprovalStatus,
                                permit.ArchivedAt,
                            }
                        ),
                    }
                );
            }
            catch
            {
                // Never throw from audit recording path
            }
        }

        public void RecordUserActivity(
            ApplicationDbContext db,
            string username,
            string displayName,
            string actionType,
            string actionLabel,
            string message,
            string source,
            string? recordedBy,
            DateTime occurredAt,
            string? actualActorUsername = null,
            bool actedUnderDelegation = false,
            string? delegatedFromUsername = null,
            int? delegationId = null,
            string? tenantId = null
        )
        {
            var activityTenantId = string.IsNullOrWhiteSpace(tenantId)
                ? db.CurrentTenantId
                : tenantId.Trim();
            db.UserActivities.Add(
                new UserActivity
                {
                    TenantId = activityTenantId,
                    Username = username,
                    DisplayName = displayName,
                    ActionType = actionType,
                    ActionLabel = actionLabel,
                    Message = message,
                    Source = source,
                    RecordedBy = recordedBy ?? string.Empty,
                    OccurredAt = occurredAt,
                }
            );

            try
            {
                db.AuditLogs.Add(
                    new AuditLog
                    {
                        TenantId = activityTenantId,
                        Username = username,
                        ActualActorUsername = actualActorUsername ?? recordedBy ?? username,
                        ActionType = actionType,
                        ActionLabel = actionLabel,
                        EntityType = "User",
                        EntityId = username,
                        Message = message,
                        Source = source,
                        RecordedBy = recordedBy ?? string.Empty,
                        OccurredAt = occurredAt,
                        Success = true,
                        ActedUnderDelegation = actedUnderDelegation,
                        DelegatedFromUsername = delegatedFromUsername ?? string.Empty,
                        DelegationId = delegationId,
                    }
                );
            }
            catch
            {
                // ignore audit failures
            }
        }

        public void NotifyPermitManagerOfActivity(
            ApplicationDbContext db,
            Permit permit,
            string? performedBy,
            string source,
            DateTime occurredAt,
            string message,
            string actionType,
            string actionLabel
        )
        {
            var approver = ResolvePermitApprover(db, permit);
            if (approver == null)
            {
                return;
            }

            RecordUserActivity(
                db,
                approver.Username,
                approver.DisplayName,
                actionType,
                actionLabel,
                message,
                source,
                performedBy,
                occurredAt
            );
        }

        private static UserAccount? ResolvePermitApprover(ApplicationDbContext db, Permit permit)
        {
            var department = db
                .Departments.AsNoTracking()
                .FirstOrDefault(x =>
                    x.Name == permit.DepartmentName || x.Name == permit.EmployeeDepartment
                );

            var manager = ResolveDepartmentManager(db, department?.ManagerUsername);
            if (manager != null)
            {
                return manager;
            }

            return db.UserAccounts.AsNoTracking()
                    .Where(x =>
                        x.IsActive && x.Role == AppRoles.GeneralManager && x.CanApprovePermit
                    )
                    .OrderBy(x => x.DisplayName)
                    .FirstOrDefault()
                ?? db.UserAccounts.AsNoTracking()
                    .FirstOrDefault(x => x.IsActive && x.CanApprovePermit);
        }

        private static UserAccount? ResolveDepartmentManager(
            ApplicationDbContext db,
            string? username
        )
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var manager = db
                .UserAccounts.AsNoTracking()
                .FirstOrDefault(x => x.Username == username);
            if (manager == null || !manager.IsActive || !manager.CanApprovePermit)
            {
                return null;
            }

            return manager;
        }
    }
}
