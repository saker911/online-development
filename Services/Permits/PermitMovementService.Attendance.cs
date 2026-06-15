using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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

namespace VehiclePermitSystemWeb.Services.Permits
{
    public sealed partial class PermitMovementService
    {
        private void RecordLateAttendanceIfNeeded(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            string source,
            DateTime occurredAt,
            PermitScanAuditContext? auditContext
        )
        {
            if (!permit.IsPermanentPermit || permit.IsVisitorPermit)
            {
                return;
            }

            var workHours = GetWorkHoursSettings();
            if (
                !TryGetMostRecentShiftWindow(
                    occurredAt,
                    workHours,
                    out var shiftStart,
                    out var shiftEnd
                )
                || occurredAt > shiftEnd
            )
            {
                return;
            }

            var allowedUntil = shiftStart.Add(workHours.AttendanceGrace);
            if (occurredAt <= allowedUntil)
            {
                return;
            }

            if (HasAttendanceMarkedForShift(db, permit.PermitNumber, shiftStart, occurredAt))
            {
                return;
            }

            var lateMinutes = GetLateMinutes(occurredAt - allowedUntil);
            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "LateAttendance",
                "تأخر حضور",
                $"تم تسجيل حضور {permit.DriverName} متأخرًا عن بداية الدوام بمقدار {lateMinutes} دقيقة بعد فترة السماح.",
                source,
                recordedBy,
                occurredAt,
                reasonCode: "LateAttendance",
                lateMinutes: lateMinutes,
                auditContext: BuildAuditContext(auditContext, source, null, "Completed")
            );
        }

        private void RecordLateCheckoutIfNeeded(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            string source,
            DateTime occurredAt,
            PermitScanAuditContext? auditContext
        )
        {
            if (!permit.IsPermanentPermit || permit.IsVisitorPermit)
            {
                return;
            }

            var workHours = GetWorkHoursSettings();
            if (
                !TryGetMostRecentShiftWindow(
                    occurredAt,
                    workHours,
                    out var shiftStart,
                    out var shiftEnd
                )
            )
            {
                return;
            }

            var allowedUntil = shiftEnd.Add(workHours.WorkEndExitGrace);
            if (occurredAt <= allowedUntil)
            {
                return;
            }

            if (!HasEntryDuringShift(db, permit.PermitNumber, shiftStart, shiftEnd))
            {
                return;
            }

            if (HasLateCheckoutMarkedForShift(db, permit.PermitNumber, shiftStart))
            {
                return;
            }

            var lateMinutes = GetLateMinutes(occurredAt - allowedUntil);
            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "LateCheckout",
                "تأخر انصراف",
                $"تم تسجيل انصراف {permit.DriverName} بعد فترة إغلاق الانصراف بمقدار {lateMinutes} دقيقة.",
                source,
                recordedBy,
                occurredAt,
                reasonCode: "LateCheckout",
                lateMinutes: lateMinutes,
                auditContext: BuildAuditContext(auditContext, source, null, "Completed")
            );
        }

        private static bool TryGetMostRecentShiftWindow(
            DateTime referenceTime,
            WorkHoursSettings workHours,
            out DateTime shiftStart,
            out DateTime shiftEnd
        )
        {
            shiftStart = default;
            shiftEnd = default;
            var officialWorkDays = AdministrationWorkSchedule.ParseOfficialWorkDays(
                workHours.OfficialWorkDaysCsv
            );
            if (officialWorkDays.Count == 0)
            {
                return false;
            }

            for (var offset = 0; offset <= 14; offset++)
            {
                var shiftDate = referenceTime.Date.AddDays(-offset);
                if (!officialWorkDays.Contains(shiftDate.DayOfWeek))
                {
                    continue;
                }

                var candidateStart = shiftDate.Add(workHours.StartTime.ToTimeSpan());
                var candidateEnd =
                    workHours.StartTime <= workHours.EndTime
                        ? shiftDate.Add(workHours.EndTime.ToTimeSpan())
                        : shiftDate.AddDays(1).Add(workHours.EndTime.ToTimeSpan());

                if (candidateStart <= referenceTime)
                {
                    shiftStart = candidateStart;
                    shiftEnd = candidateEnd;
                    return true;
                }
            }

            return false;
        }

        private static bool HasAttendanceMarkedForShift(
            ApplicationDbContext db,
            string permitNumber,
            DateTime shiftStart,
            DateTime occurredAt
        )
        {
            var nextShift = shiftStart.Date.AddDays(1);
            return db.PermitActivities.Any(activity =>
                activity.PermitNumber == permitNumber
                && activity.OccurredAt >= shiftStart.Date
                && activity.OccurredAt < nextShift
                && activity.OccurredAt < occurredAt
                && (
                    activity.ActionType == "Entry"
                    || activity.ActionType == "LateAttendance"
                    || activity.ActionType == "WorkEndEntry"
                )
            );
        }

        private static bool HasLateCheckoutMarkedForShift(
            ApplicationDbContext db,
            string permitNumber,
            DateTime shiftStart
        )
        {
            var nextShift = shiftStart.Date.AddDays(1);
            return db.PermitActivities.Any(activity =>
                activity.PermitNumber == permitNumber
                && activity.OccurredAt >= shiftStart.Date
                && activity.OccurredAt < nextShift
                && activity.ActionType == "LateCheckout"
            );
        }

        private static bool HasEntryDuringShift(
            ApplicationDbContext db,
            string permitNumber,
            DateTime shiftStart,
            DateTime shiftEnd
        )
        {
            return db.PermitActivities.Any(activity =>
                activity.PermitNumber == permitNumber
                && activity.OccurredAt >= shiftStart
                && activity.OccurredAt <= shiftEnd
                && (
                    activity.ActionType == "Entry"
                    || activity.ActionType == "LateAttendance"
                    || activity.ActionType == "WorkEndEntry"
                )
            );
        }

    }
}
