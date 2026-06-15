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
    public sealed class PermitMonitoringService : IPermitMonitoringService
    {
        private readonly ILeavePolicyService _leavePolicyService;
        private readonly IPermitAuditService _permitAuditService;

        public PermitMonitoringService(
            ILeavePolicyService leavePolicyService,
            IPermitAuditService permitAuditService
        )
        {
            _leavePolicyService = leavePolicyService;
            _permitAuditService = permitAuditService;
        }

        public int HandleReturnMonitoring(
            ApplicationDbContext db,
            DateTime referenceTime,
            string? performedBy,
            string source,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string officialWorkDaysCsv,
            TimeSpan lateReturnGrace
        )
        {
            return 0;
        }

        private void StopPermitForViolation(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            string source,
            DateTime occurredAt,
            string actionType,
            string actionLabel,
            string message,
            int? lateMinutes = null
        )
        {
            permit.ApprovalStatus = "Stopped";
            permit.ArchivedAt = occurredAt;
            _leavePolicyService.ResetLeaveState(permit);
            permit.CurrentState = "Outside";
            permit.UnauthorizedExitWarningCount = 0;
            permit.LastUnauthorizedExitWarningAt = null;

            if (!permit.ExpiresAt.HasValue || permit.ExpiresAt > occurredAt)
            {
                permit.ExpiresAt = occurredAt;
            }

            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                actionType,
                actionLabel,
                message,
                source,
                recordedBy,
                occurredAt,
                reasonCode: actionType,
                lateMinutes: lateMinutes
            );
            _permitAuditService.RecordUserActivity(
                db,
                recordedBy ?? "system",
                permit.DriverName,
                "PermitStopped",
                "إيقاف تصريح",
                message,
                source,
                recordedBy,
                occurredAt
            );
        }

        private static int GetLateMinutes(TimeSpan delay)
        {
            return Math.Max(1, (int)Math.Ceiling(delay.TotalMinutes));
        }

        private static string DescribeDelay(TimeSpan delay)
        {
            var totalMinutes = Math.Max(1, (int)Math.Ceiling(delay.TotalMinutes));
            if (totalMinutes < 60)
            {
                return $"{totalMinutes} دقيقة";
            }

            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return minutes == 0 ? $"{hours} ساعة" : $"{hours} ساعة و{minutes} دقيقة";
        }
    }
}
