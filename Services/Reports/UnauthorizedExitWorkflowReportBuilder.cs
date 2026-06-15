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

namespace VehiclePermitSystemWeb.Services.Reports
{
    public static class UnauthorizedExitWorkflowReportBuilder
    {
        public static List<UnauthorizedExitReviewSequenceViewModel> BuildUnauthorizedExitReviewQueue(
            IEnumerable<PermitActivity> activities,
            IReadOnlyDictionary<string, Permit> permitsLookup
        )
        {
            return activities
                .Where(activity => !string.IsNullOrWhiteSpace(activity.SequenceId))
                .GroupBy(activity => activity.SequenceId)
                .Select(group =>
                {
                    var orderedActivities = group
                        .OrderBy(activity => activity.OccurredAt)
                        .ThenBy(activity => activity.Id)
                        .ToList();
                    var latestActivity = orderedActivities.Last();
                    var reviewActivity = orderedActivities.LastOrDefault(activity =>
                        string.Equals(
                            activity.ReasonCode,
                            "UnauthorizedExitNeedsReview",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                    if (
                        reviewActivity == null
                        || !string.Equals(
                            latestActivity.ReasonCode,
                            "UnauthorizedExitNeedsReview",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return null;
                    }

                    var firstAttempt =
                        orderedActivities.FirstOrDefault(activity =>
                            string.Equals(
                                activity.ReasonCode,
                                "PendingUnauthorizedExit",
                                StringComparison.OrdinalIgnoreCase
                            )
                        ) ?? orderedActivities.First();

                    permitsLookup.TryGetValue(reviewActivity.PermitNumber, out var permit);

                    return new UnauthorizedExitReviewSequenceViewModel
                    {
                        SequenceId = group.Key,
                        PermitNumber = reviewActivity.PermitNumber,
                        DriverName = reviewActivity.DriverName,
                        DepartmentName = reviewActivity.DepartmentName,
                        ApprovalStatusDisplay = permit?.ApprovalStatusDisplay ?? "-",
                        ClassificationStatus = reviewActivity.ClassificationStatus,
                        GateName = string.IsNullOrWhiteSpace(firstAttempt.GateName)
                            ? firstAttempt.Source
                            : firstAttempt.GateName,
                        OperatorDisplay = BuildOperatorDisplay(firstAttempt),
                        SummaryMessage = reviewActivity.Message,
                        FirstAttemptAt = firstAttempt.OccurredAt,
                        ReviewRaisedAt = reviewActivity.OccurredAt,
                        ActivityCount = orderedActivities.Count,
                        WarningCount = permit?.UnauthorizedExitWarningCount ?? 0,
                    };
                })
                .Where(item => item != null)
                .Select(item => item!)
                .OrderByDescending(item => item.ReviewRaisedAt ?? DateTime.MinValue)
                .ThenBy(item => item.PermitNumber)
                .ToList();
        }

        public static List<UnauthorizedExitWorkflowItemViewModel> BuildUnauthorizedExitWorkflowItems(
            IEnumerable<PermitActivity> activities,
            IReadOnlyDictionary<string, Permit> permitsLookup
        )
        {
            return activities
                .Where(activity => !string.IsNullOrWhiteSpace(activity.SequenceId))
                .GroupBy(activity => activity.SequenceId)
                .Select(group =>
                {
                    var orderedActivities = group
                        .OrderBy(activity => activity.OccurredAt)
                        .ThenBy(activity => activity.Id)
                        .ToList();
                    if (
                        !orderedActivities.Any(activity =>
                            string.Equals(
                                activity.ReasonCode,
                                "PendingUnauthorizedExit",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || string.Equals(
                                activity.ReasonCode,
                                "UnauthorizedExitNeedsReview",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || string.Equals(
                                activity.ReasonCode,
                                "UnauthorizedExitStopped",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || string.Equals(
                                activity.ReasonCode,
                                "DeniedAttemptClosed",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || string.Equals(
                                activity.ReasonCode,
                                "AdministrativeReviewDismissed",
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
                        )
                    )
                    {
                        return null;
                    }

                    var latestActivity = orderedActivities.Last();
                    var firstAttempt =
                        orderedActivities.FirstOrDefault(activity =>
                            string.Equals(
                                activity.ReasonCode,
                                "PendingUnauthorizedExit",
                                StringComparison.OrdinalIgnoreCase
                            )
                        ) ?? orderedActivities.First();
                    var reviewActivity = orderedActivities.LastOrDefault(activity =>
                        string.Equals(
                            activity.ReasonCode,
                            "UnauthorizedExitNeedsReview",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                    permitsLookup.TryGetValue(latestActivity.PermitNumber, out var permit);

                    var workflowStatus = DetermineUnauthorizedExitWorkflowStatus(latestActivity);
                    if (string.IsNullOrWhiteSpace(workflowStatus))
                    {
                        return null;
                    }

                    return new UnauthorizedExitWorkflowItemViewModel
                    {
                        SequenceId = group.Key,
                        PermitNumber = latestActivity.PermitNumber,
                        DriverName = latestActivity.DriverName,
                        DepartmentName = latestActivity.DepartmentName,
                        ApprovalStatusDisplay = permit?.ApprovalStatusDisplay ?? "-",
                        WorkflowStatus = workflowStatus,
                        WorkflowStatusDisplay = GetUnauthorizedExitWorkflowStatusDisplay(
                            workflowStatus
                        ),
                        FinalClassificationStatus = latestActivity.ClassificationStatus,
                        FinalClassificationDisplay = GetClassificationStatusDisplay(
                            latestActivity.ClassificationStatus
                        ),
                        GateName = string.IsNullOrWhiteSpace(firstAttempt.GateName)
                            ? firstAttempt.Source
                            : firstAttempt.GateName,
                        OperatorDisplay = BuildOperatorDisplay(firstAttempt),
                        SummaryMessage = latestActivity.Message,
                        FirstAttemptAt = firstAttempt.OccurredAt,
                        LastUpdatedAt = latestActivity.OccurredAt,
                        ReviewRaisedAt = reviewActivity?.OccurredAt,
                        ActivityCount = orderedActivities.Count,
                        WarningCount = permit?.UnauthorizedExitWarningCount ?? 0,
                        CanResolveReview = string.Equals(
                            workflowStatus,
                            "UnderReview",
                            StringComparison.OrdinalIgnoreCase
                        ),
                        IsStopped = string.Equals(
                            workflowStatus,
                            "Stopped",
                            StringComparison.OrdinalIgnoreCase
                        ),
                        IsClosed = string.Equals(
                            workflowStatus,
                            "Closed",
                            StringComparison.OrdinalIgnoreCase
                        ),
                    };
                })
                .Where(item => item != null)
                .Select(item => item!)
                .ToList();
        }

        private static string DetermineUnauthorizedExitWorkflowStatus(PermitActivity latestActivity)
        {
            if (
                string.Equals(
                    latestActivity.ReasonCode,
                    "PendingUnauthorizedExit",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    latestActivity.ClassificationStatus,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "Pending";
            }

            if (
                string.Equals(
                    latestActivity.ReasonCode,
                    "UnauthorizedExitNeedsReview",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    latestActivity.ClassificationStatus,
                    "NeedsAdministrativeReview",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "UnderReview";
            }

            if (
                string.Equals(
                    latestActivity.ReasonCode,
                    "UnauthorizedExitStopped",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    latestActivity.ClassificationStatus,
                    "Stopped",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "Stopped";
            }

            if (
                string.Equals(
                    latestActivity.ReasonCode,
                    "DeniedAttemptClosed",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    latestActivity.ReasonCode,
                    "AdministrativeReviewDismissed",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    latestActivity.ReasonCode,
                    "AdministrativeReviewConfirmed",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    latestActivity.ClassificationStatus,
                    "ClosedWithoutViolation",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    latestActivity.ClassificationStatus,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    latestActivity.ClassificationStatus,
                    "ConfirmedViolation",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "Closed";
            }

            return string.Empty;
        }

        private static string GetClassificationStatusDisplay(string? status)
        {
            return status switch
            {
                "Pending" => "معلّقة",
                "ClosedWithoutViolation" => "أغلقت دون مخالفة",
                "ConfirmedViolation" => "مخالفة مؤكدة",
                "NeedsAdministrativeReview" => "تحت المراجعة",
                "Completed" => "مغلقة",
                "Stopped" => "موقوفة",
                _ => string.IsNullOrWhiteSpace(status) ? "-" : status,
            };
        }

        private static string GetUnauthorizedExitWorkflowStatusDisplay(string status)
        {
            return status switch
            {
                "Pending" => "حالات معلقة",
                "UnderReview" => "تحت المراجعة",
                "Stopped" => "موقوفة",
                "Closed" => "مغلقة",
                _ => "كل الحالات",
            };
        }

        private static string BuildOperatorDisplay(PermitActivity activity)
        {
            if (!string.IsNullOrWhiteSpace(activity.GateOperatorName))
            {
                return string.IsNullOrWhiteSpace(activity.GateOperatorAccount)
                    ? activity.GateOperatorName
                    : $"{activity.GateOperatorName} ({activity.GateOperatorAccount})";
            }

            return string.IsNullOrWhiteSpace(activity.RecordedBy) ? "-" : activity.RecordedBy;
        }
    }
}
