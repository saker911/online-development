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
        private static bool HasPendingUnauthorizedExit(Permit permit)
        {
            return permit.PendingUnauthorizedExitAt.HasValue
                && !string.IsNullOrWhiteSpace(permit.PendingUnauthorizedExitSequenceId);
        }

        private static string EnsurePendingUnauthorizedExit(Permit permit, DateTime occurredAt)
        {
            if (!permit.PendingUnauthorizedExitAt.HasValue)
            {
                permit.PendingUnauthorizedExitAt = occurredAt;
            }

            if (string.IsNullOrWhiteSpace(permit.PendingUnauthorizedExitSequenceId))
            {
                permit.PendingUnauthorizedExitSequenceId = Guid.NewGuid().ToString("N");
            }

            return permit.PendingUnauthorizedExitSequenceId;
        }

        private static void ClearPendingUnauthorizedExit(Permit permit)
        {
            permit.PendingUnauthorizedExitAt = null;
            permit.PendingUnauthorizedExitSequenceId = string.Empty;
        }

        private static void PreparePermitForAccessMode(Permit permit)
        {
            permit.NormalizeAccessModeState();
            if (!permit.IsFullAccessPermit)
            {
                return;
            }

            permit.ExpectedReturnTime = null;
            permit.PendingExitRequest = false;
            permit.LeaveWindowStartAt = null;
            permit.LeaveWindowEndAt = null;
            permit.LeaveReason = string.Empty;
            ResetPenaltyStateForFullAccess(permit);
        }

        private static void ResetPenaltyStateForFullAccess(Permit permit)
        {
            permit.PendingUnauthorizedExitAt = null;
            permit.PendingUnauthorizedExitSequenceId = string.Empty;
            permit.UnauthorizedExitWarningCount = 0;
            permit.LastUnauthorizedExitWarningAt = null;
            permit.LateReturnWarningCount = 0;
            permit.LastLateReturnWarningForExpectedReturnTime = null;
        }

        private void ClosePendingUnauthorizedExitWithoutViolation(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            string source,
            DateTime occurredAt,
            string message,
            PermitScanAuditContext? auditContext
        )
        {
            if (!HasPendingUnauthorizedExit(permit))
            {
                return;
            }

            var sequenceId = permit.PendingUnauthorizedExitSequenceId;
            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "DeniedAttemptClosed",
                "إغلاق محاولة معلقة",
                message,
                source,
                recordedBy,
                occurredAt,
                reasonCode: "DeniedAttemptClosed",
                auditContext: BuildAuditContext(
                    auditContext,
                    source,
                    sequenceId,
                    "ClosedWithoutViolation"
                )
            );
            ClearPendingUnauthorizedExit(permit);
        }

        private bool PromoteStalePendingUnauthorizedExitSequence(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            DateTime occurredAt,
            string source,
            PermitScanAuditContext? auditContext
        )
        {
            if (
                !HasPendingUnauthorizedExit(permit)
                || !permit.PendingUnauthorizedExitAt.HasValue
                || permit.PendingUnauthorizedExitAt.Value.Date >= occurredAt.Date
            )
            {
                return false;
            }

            var pendingSequenceId = permit.PendingUnauthorizedExitSequenceId;
            _permitAuditService.RecordPermitActivity(
                db,
                permit,
                "UnauthorizedExitNeedsReview",
                "خروج يحتاج مراجعة إدارية",
                $"استمرت محاولة الخروج المعلقة لـ {permit.DriverName} من يوم سابق دون حسم، وتم تحويلها إلى مراجعة إدارية عند أول حركة لاحقة.",
                source,
                recordedBy,
                occurredAt,
                reasonCode: "UnauthorizedExitNeedsReview",
                auditContext: BuildAuditContext(
                    auditContext,
                    source,
                    pendingSequenceId,
                    "NeedsAdministrativeReview"
                )
            );

            _permitAuditService.NotifyPermitManagerOfActivity(
                db,
                permit,
                recordedBy,
                source,
                occurredAt,
                $"تم تحويل محاولة خروج سابقة لـ {permit.DriverName} إلى مراجعة إدارية عند تسجيل حركة جديدة.",
                "UnauthorizedExitNeedsReview",
                "تحويل لمراجعة إدارية"
            );

            ClearPendingUnauthorizedExit(permit);
            db.SaveChanges();

            return PersistUnauthorizedExitViolationSummary(
                db,
                permit,
                recordedBy,
                source,
                occurredAt,
                pendingSequenceId,
                auditContext,
                $"تم إيقاف التصريح بعد تسجيل مخالفات خروج غير مصرح في 3 أيام مختلفة لـ {permit.DriverName}."
            );
        }

        private bool PersistUnauthorizedExitViolationSummary(
            ApplicationDbContext db,
            Permit permit,
            string? recordedBy,
            string source,
            DateTime occurredAt,
            string? sequenceId,
            PermitScanAuditContext? auditContext,
            string stopMessage,
            string? stoppedCurrentState = null
        )
        {
            RefreshUnauthorizedExitViolationSummary(db, permit);
            if (
                permit.UnauthorizedExitWarningCount >= 3
                && !string.Equals(
                    permit.ApprovalStatus,
                    "Stopped",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                StopPermitForUnauthorizedExitViolation(
                    db,
                    permit,
                    recordedBy,
                    source,
                    occurredAt,
                    stopMessage,
                    BuildAuditContext(auditContext, source, sequenceId, "Stopped"),
                    stoppedCurrentState
                );
            }

            db.SaveChanges();
            return string.Equals(
                permit.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private void RefreshUnauthorizedExitViolationSummary(ApplicationDbContext db, Permit permit)
        {
            permit.NormalizeAccessModeState();
            if (!permit.IsEntryOnlyPermit)
            {
                ResetPenaltyStateForFullAccess(permit);
                return;
            }

            var violationDays = new Dictionary<DateOnly, DateTime>();
            var sequenceGroups = db
                .PermitActivities.AsNoTracking()
                .Where(activity =>
                    activity.PermitNumber == permit.PermitNumber
                    && !string.IsNullOrWhiteSpace(activity.SequenceId)
                )
                .OrderBy(activity => activity.OccurredAt)
                .ThenBy(activity => activity.Id)
                .ToList()
                .GroupBy(activity => activity.SequenceId);

            foreach (var sequenceGroup in sequenceGroups)
            {
                var orderedActivities = sequenceGroup.ToList();
                if (!SequenceCountsTowardUnauthorizedExitPenalty(orderedActivities))
                {
                    continue;
                }

                var violationAt = GetUnauthorizedExitViolationDate(orderedActivities);
                var violationDay = DateOnly.FromDateTime(violationAt);
                if (
                    !violationDays.TryGetValue(violationDay, out var existingViolationAt)
                    || violationAt > existingViolationAt
                )
                {
                    violationDays[violationDay] = violationAt;
                }
            }

            permit.UnauthorizedExitWarningCount = violationDays.Count;
            permit.LastUnauthorizedExitWarningAt =
                violationDays.Count == 0 ? null : violationDays.Values.Max();
        }

        private static bool SequenceCountsTowardUnauthorizedExitPenalty(
            IReadOnlyList<PermitActivity> activities
        )
        {
            if (activities.Count == 0)
            {
                return false;
            }

            if (
                activities.Any(activity =>
                    string.Equals(
                        activity.ReasonCode,
                        "DeniedAttemptClosed",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        activity.ReasonCode,
                        "AdministrativeReviewDismissed",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                return false;
            }

            return activities.Any(activity =>
                string.Equals(
                    activity.ReasonCode,
                    "UnauthorizedExitConfirmed",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    activity.ReasonCode,
                    "UnauthorizedExitNeedsReview",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    activity.ReasonCode,
                    "AdministrativeReviewConfirmed",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    activity.ReasonCode,
                    "NoCheckoutViolation",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    activity.ReasonCode,
                    "NoReturnViolation",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    activity.ReasonCode,
                    "UnauthorizedExitStopped",
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }

        private static DateTime GetUnauthorizedExitViolationDate(
            IReadOnlyList<PermitActivity> activities
        )
        {
            return activities
                    .FirstOrDefault(activity =>
                        string.Equals(
                            activity.ReasonCode,
                            "PendingUnauthorizedExit",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    ?.OccurredAt
                ?? activities[0].OccurredAt;
        }

    }
}
