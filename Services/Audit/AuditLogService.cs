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

namespace VehiclePermitSystemWeb.Services.Audit
{
    public sealed class AuditLogService : IAuditLogService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;

        public AuditLogService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
        }

        public void Record(
            string username,
            string actionType,
            string actionLabel,
            string entityType,
            string entityId,
            string message,
            string source,
            bool success = true,
            string? ipAddress = null,
            string? actualActorUsername = null,
            bool actedUnderDelegation = false,
            string? delegatedFromUsername = null,
            int? delegationId = null,
            string? beforeJson = null,
            string? afterJson = null
        )
        {
            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                db.AuditLogs.Add(
                    new AuditLog
                    {
                        Username = username,
                        ActionType = actionType,
                        ActionLabel = actionLabel,
                        EntityType = entityType,
                        EntityId = entityId,
                        Message = PersonalDataSanitizer.SanitizeAuditText(message),
                        Source = source,
                        RecordedBy = username,
                        OccurredAt = _systemClock.UtcNow,
                        IpAddress = ipAddress ?? string.Empty,
                        Success = success,
                        ActualActorUsername = actualActorUsername ?? username,
                        ActedUnderDelegation = actedUnderDelegation,
                        DelegatedFromUsername = delegatedFromUsername ?? string.Empty,
                        DelegationId = delegationId,
                        BeforeJson = PersonalDataSanitizer.SanitizeAuditJson(beforeJson),
                        AfterJson = PersonalDataSanitizer.SanitizeAuditJson(afterJson),
                    }
                );
                db.SaveChanges();
            }
            catch
            {
                // لا نوقف العملية الأساسية أبدًا بسبب فشل تسجيل Audit
            }
        }
    }
}
