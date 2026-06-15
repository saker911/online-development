using System.Globalization;
using System.Text;
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
    public class ReportsDashboardService : IReportsDashboardService
    {
        private readonly IPermitService _permitService;
        private readonly IVisitService _visitService;
        private readonly IAccessControlService _accessControl;
        private readonly IUserAdminService _userAdminService;
        private readonly ISystemClock _systemClock;

        public ReportsDashboardService(
            IPermitService permitService,
            IVisitService visitService,
            ISystemClock systemClock,
            IAccessControlService accessControl,
            IUserAdminService userAdminService
        )
        {
            _permitService = permitService;
            _visitService = visitService;
            _systemClock = systemClock;
            _accessControl = accessControl;
            _userAdminService = userAdminService;
        }

        private static int CalculateCommitmentPercent(int total, int lateCount)
        {
            if (total <= 0)
            {
                return 100;
            }

            var committed = Math.Max(0, total - lateCount);
            return (int)Math.Round(committed * 100m / total, MidpointRounding.AwayFromZero);
        }

        private static (int Total, int Late, int Percent) BuildCommitmentMetric(
            IEnumerable<PermitActivity> activities,
            string lateActionType,
            params string[] countedActionTypes
        )
        {
            var counted = new HashSet<string>(countedActionTypes, StringComparer.OrdinalIgnoreCase);
            var groups = activities
                .Where(activity => counted.Contains(activity.ActionType))
                .GroupBy(activity => new { activity.PermitNumber, Date = activity.OccurredAt.Date })
                .ToList();
            var total = groups.Count;
            var late = groups.Count(group =>
                group.Any(activity =>
                    string.Equals(activity.ActionType, lateActionType, StringComparison.OrdinalIgnoreCase)
                )
            );

            return (total, late, CalculateCommitmentPercent(total, late));
        }

        public ReportsDashboardViewModel BuildReportsHomeModel(
            bool canViewPermits,
            bool canViewVisits,
            string? currentUser
        )
        {
            var allPermits = canViewPermits
                ? _permitService.GetAllPermits(currentUser).ToList()
                : new List<Permit>();
            var allVisits = new List<Visit>();
            if (canViewVisits)
            {
                var rawVisits = _visitService.GetAllVisits();
                var currentUserAccount = _userAdminService.GetUserAccount(
                    currentUser ?? string.Empty
                );
                allVisits = _accessControl
                    .FilterVisitsForUser(rawVisits, currentUserAccount)
                    .ToList();
            }
            var now = _systemClock.LocalNow;
            var dayStart = now.Date;
            var weekStart = GetWeekStart(now);
            var monthStart = new DateTime(now.Year, now.Month, 1);

            return new ReportsDashboardViewModel
            {
                ActiveSection = "Home",
                TotalPermits = allPermits.Count,
                PendingPermits = allPermits.Count(p =>
                    p.ApprovalStatus == "Pending" && !p.ArchivedAt.HasValue
                ),
                StoppedPermitsCount = allPermits.Count(p =>
                    string.Equals(p.ApprovalStatus, "Stopped", StringComparison.OrdinalIgnoreCase)
                ),
                TodayPermitActivities = _permitService
                    .GetRecentPermitActivities(50, currentUser)
                    .Count(a => a.OccurredAt >= dayStart),
                TotalVisits = allVisits.Count,
                PendingApprovalVisits = allVisits.Count(v => v.ApprovalStatus == "Pending"),
                InsideVisits = allVisits.Count(v =>
                    string.Equals(v.Status, "Inside", StringComparison.OrdinalIgnoreCase)
                ),
                VisitMetrics = new List<PeriodMetricViewModel>
                {
                    ReportMetricBuilder.BuildMetric(
                        "يومي",
                        allVisits.Count(v => v.VisitDate >= dayStart),
                        "الزيارات المسجلة اليوم"
                    ),
                    ReportMetricBuilder.BuildMetric(
                        "أسبوعي",
                        allVisits.Count(v => v.VisitDate >= weekStart),
                        "الزيارات المسجلة من بداية الأسبوع"
                    ),
                    ReportMetricBuilder.BuildMetric(
                        "شهري",
                        allVisits.Count(v => v.VisitDate >= monthStart),
                        "الزيارات المسجلة من بداية الشهر"
                    ),
                },
            };
        }

        public ReportsDashboardViewModel BuildPermitsDashboardModel(
            string permitTypeFilter,
            string? reportQuery,
            string? selectedPermitNumber,
            int page,
            int pageSize,
            string? currentUser
        )
        {
            var normalizedPermitTypeFilter = ReportFilterNormalizer.NormalizePermitTypeFilter(
                permitTypeFilter
            );
            var normalizedReportQuery = reportQuery?.Trim() ?? string.Empty;
            var normalizedSelectedPermitNumber = selectedPermitNumber?.Trim() ?? string.Empty;
            var allPermits = _permitService.GetAllPermits(currentUser).ToList();
            var matchingPermits = FindMatchingPermits(allPermits, normalizedReportQuery);
            var selectedPermit = ResolveSelectedPermit(
                allPermits,
                matchingPermits,
                normalizedSelectedPermitNumber
            );
            var permits = ApplyPermitTypeFilter(allPermits, normalizedPermitTypeFilter)
                .OrderByDescending(p => p.ExpiresAt ?? DateTime.MinValue)
                .ToList();
            var normalizedPageSize = PageSizeNormalizer.NormalizePageSize(pageSize);
            var totalPages = Math.Max(
                1,
                (int)Math.Ceiling(permits.Count / (double)normalizedPageSize)
            );
            var currentPage = Math.Min(Math.Max(page, 1), totalPages);
            var pagedPermits = permits
                .Skip((currentPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();
            var recentPermitActivities = _permitService
                .GetRecentPermitActivities(20, currentUser)
                .ToList();
            var now = _systemClock.LocalNow;
            var dayStart = now.Date;
            var weekStart = GetWeekStart(now);
            var monthStart = new DateTime(now.Year, now.Month, 1);
            return new ReportsDashboardViewModel
            {
                SelectedPermitNumber =
                    selectedPermit?.PermitNumber ?? normalizedSelectedPermitNumber,
                Permits = pagedPermits,
                MatchingPermits = matchingPermits,
                PermitHolders = permits
                    .Where(p => p.ApprovalStatus == "Approved" || p.ApprovalStatus == "Out")
                    .OrderBy(p => p.DriverName)
                    .ToList(),
                PendingApprovalPermits = allPermits
                    .Where(p => p.ApprovalStatus == "Pending" && !p.ArchivedAt.HasValue)
                    .OrderBy(p => p.ExpiresAt ?? DateTime.MaxValue)
                    .ToList(),
                SelectedPermit = selectedPermit,
                PermitTypeFilter = normalizedPermitTypeFilter,
                PermitTypeFilterDisplay = ReportDisplayFormatter.GetPermitTypeFilterDisplay(
                    normalizedPermitTypeFilter
                ),
                PermitPage = currentPage,
                PermitPageSize = normalizedPageSize,
                PermitTotalPages = totalPages,
                PermitTotalCount = permits.Count,
                TotalPermits = permits.Count,
                ApprovedPermits = permits.Count(p => p.ApprovalStatus == "Approved"),
                PendingPermits = permits.Count(p => p.ApprovalStatus == "Pending"),
                ExitPermits = permits.Count(p => p.PermitType == Permit.PermitTypeExit),
                PermanentPermits = permits.Count(p => p.PermitType == Permit.PermitTypePermanent),
                VisitorPermits = permits.Count(p => Permit.IsVisitorPermitType(p.PermitType)),
                TotalPermitActivities = recentPermitActivities.Count,
                TodayPermitActivities = recentPermitActivities.Count(a => a.OccurredAt >= dayStart),
                ExitMetrics = new List<PeriodMetricViewModel>
                {
                    ReportMetricBuilder.BuildMetric(
                        "يومي",
                        permits.Count(p => p.OutTime.HasValue && p.OutTime.Value >= dayStart),
                        "حركات الخروج المسجلة اليوم"
                    ),
                    ReportMetricBuilder.BuildMetric(
                        "أسبوعي",
                        permits.Count(p => p.OutTime.HasValue && p.OutTime.Value >= weekStart),
                        "حركات الخروج من بداية الأسبوع"
                    ),
                    ReportMetricBuilder.BuildMetric(
                        "شهري",
                        permits.Count(p => p.OutTime.HasValue && p.OutTime.Value >= monthStart),
                        "حركات الخروج من بداية الشهر"
                    ),
                },
            };
        }

        public ReportsDashboardViewModel BuildPermitActivityDashboardModel(
            string? activityQuery,
            string? selectedSequenceId,
            int page,
            int pageSize,
            string? currentUser
        )
        {
            var report = BuildPermitActivityReportResult(activityQuery, currentUser);
            var sequencedActivities = _permitService
                .GetSequencedPermitActivities(currentUser)
                .ToList();
            var reviewQueue =
                UnauthorizedExitWorkflowReportBuilder.BuildUnauthorizedExitReviewQueue(
                    sequencedActivities,
                    report.PermitsLookup
                );
            var normalizedPageSize = PageSizeNormalizer.NormalizePageSize(pageSize);
            var totalPages = Math.Max(
                1,
                (int)Math.Ceiling(report.Activities.Count / (double)normalizedPageSize)
            );
            var currentPage = Math.Min(Math.Max(page, 1), totalPages);
            var pagedActivities = report
                .Activities.Skip((currentPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();
            var normalizedSelectedSequenceId = selectedSequenceId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedSelectedSequenceId) && reviewQueue.Count > 0)
            {
                normalizedSelectedSequenceId = reviewQueue[0].SequenceId;
            }

            var selectedSequenceActivities = string.IsNullOrWhiteSpace(normalizedSelectedSequenceId)
                ? new List<PermitActivity>()
                : sequencedActivities
                    .Where(activity =>
                        string.Equals(
                            activity.SequenceId,
                            normalizedSelectedSequenceId,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .OrderBy(activity => activity.OccurredAt)
                    .ThenBy(activity => activity.Id)
                    .ToList();
            var attendanceMetric = BuildCommitmentMetric(
                report.Activities,
                "LateAttendance",
                "Entry",
                "LateAttendance",
                "WorkEndEntry"
            );
            var checkoutMetric = BuildCommitmentMetric(
                report.Activities,
                "LateCheckout",
                "ExitFinal",
                "ExitAuthorized",
                "WorkEndExit",
                "LateCheckout"
            );

            return new ReportsDashboardViewModel
            {
                ActiveSection = "PermitActivity",
                ActivityQuery = report.ActivityQuery,
                SelectedSequenceId = normalizedSelectedSequenceId,
                FilteredPermitActivities = pagedActivities,
                SelectedSequenceActivities = selectedSequenceActivities,
                UnauthorizedExitReviews = reviewQueue,
                SelectedUnauthorizedExitReview = reviewQueue.FirstOrDefault(item =>
                    string.Equals(
                        item.SequenceId,
                        normalizedSelectedSequenceId,
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
                PermitActivityPage = currentPage,
                PermitActivityPageSize = normalizedPageSize,
                PermitActivityTotalPages = totalPages,
                PermitActivityTotalCount = report.Activities.Count,
                TotalPermitActivities = report.Activities.Count,
                TodayPermitActivities = report.Activities.Count(item =>
                    item.OccurredAt >= _systemClock.LocalNow.Date
                ),
                AttendanceEntryCount = attendanceMetric.Total,
                LateAttendanceCount = attendanceMetric.Late,
                CheckoutExitCount = checkoutMetric.Total,
                LateCheckoutCount = checkoutMetric.Late,
                AttendanceCommitmentPercent = attendanceMetric.Percent,
                CheckoutCommitmentPercent = checkoutMetric.Percent,
                UnauthorizedExitReviewCount = reviewQueue.Count,
                SequencedPermitActivityCount = sequencedActivities
                    .Select(activity => activity.SequenceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
            };
        }

        public ReportsDashboardViewModel BuildUnauthorizedExitWorkflowDashboardModel(
            string? workflowQuery,
            string workflowStatusFilter,
            int page,
            int pageSize,
            string? currentUser
        )
        {
            var normalizedQuery = workflowQuery?.Trim() ?? string.Empty;
            var normalizedStatusFilter =
                ReportFilterNormalizer.NormalizeUnauthorizedExitWorkflowStatusFilter(
                    workflowStatusFilter
                );
            var sequencedActivities = _permitService
                .GetSequencedPermitActivities(currentUser)
                .ToList();
            var permitsLookup = _permitService
                .GetAllPermits(currentUser)
                .ToDictionary(item => item.PermitNumber, StringComparer.OrdinalIgnoreCase);

            var workflowItems = UnauthorizedExitWorkflowReportBuilder
                .BuildUnauthorizedExitWorkflowItems(sequencedActivities, permitsLookup)
                .Where(item =>
                    string.Equals(normalizedStatusFilter, "All", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        item.WorkflowStatus,
                        normalizedStatusFilter,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Where(item =>
                    string.IsNullOrWhiteSpace(normalizedQuery)
                    || TextSearchMatcher.ContainsValue(item.PermitNumber, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(item.DriverName, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(item.DepartmentName, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(item.WorkflowStatusDisplay, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(item.SummaryMessage, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(item.OperatorDisplay, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(item.GateName, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(item.SequenceId, normalizedQuery)
                )
                .OrderByDescending(item => item.LastUpdatedAt ?? DateTime.MinValue)
                .ThenBy(item => item.PermitNumber)
                .ToList();

            var normalizedPageSize = PageSizeNormalizer.NormalizePageSize(pageSize);
            var totalPages = Math.Max(
                1,
                (int)Math.Ceiling(workflowItems.Count / (double)normalizedPageSize)
            );
            var currentPage = Math.Min(Math.Max(page, 1), totalPages);

            return new ReportsDashboardViewModel
            {
                ActiveSection = "UnauthorizedExitWorkflow",
                UnauthorizedExitWorkflowQuery = normalizedQuery,
                UnauthorizedExitWorkflowStatusFilter = normalizedStatusFilter,
                UnauthorizedExitWorkflowStatusDisplay =
                    ReportDisplayFormatter.GetUnauthorizedExitWorkflowStatusDisplay(
                        normalizedStatusFilter
                    ),
                UnauthorizedExitWorkflowItems = workflowItems
                    .Skip((currentPage - 1) * normalizedPageSize)
                    .Take(normalizedPageSize)
                    .ToList(),
                UnauthorizedExitWorkflowPage = currentPage,
                UnauthorizedExitWorkflowPageSize = normalizedPageSize,
                UnauthorizedExitWorkflowTotalPages = totalPages,
                UnauthorizedExitWorkflowTotalCount = workflowItems.Count,
                UnauthorizedExitWorkflowPendingCount = workflowItems.Count(item =>
                    string.Equals(
                        item.WorkflowStatus,
                        "Pending",
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
                UnauthorizedExitWorkflowUnderReviewCount = workflowItems.Count(item =>
                    string.Equals(
                        item.WorkflowStatus,
                        "UnderReview",
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
                UnauthorizedExitWorkflowStoppedCount = workflowItems.Count(item =>
                    string.Equals(
                        item.WorkflowStatus,
                        "Stopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
                UnauthorizedExitWorkflowClosedCount = workflowItems.Count(item =>
                    string.Equals(item.WorkflowStatus, "Closed", StringComparison.OrdinalIgnoreCase)
                ),
            };
        }

        public ReportsDashboardViewModel BuildStoppedPermitsDashboardModel(
            string? stoppedQuery,
            int page,
            int pageSize,
            string? currentUser
        )
        {
            var normalizedStoppedQuery = stoppedQuery?.Trim() ?? string.Empty;
            var stoppedPermits = _permitService
                .GetAllPermits(currentUser)
                .Where(permit =>
                    string.Equals(
                        permit.ApprovalStatus,
                        "Stopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Select(permit => MapStoppedPermit(permit, currentUser))
                .Where(item =>
                    string.IsNullOrWhiteSpace(normalizedStoppedQuery)
                    || TextSearchMatcher.ContainsValue(item.PermitNumber, normalizedStoppedQuery)
                    || TextSearchMatcher.ContainsValue(item.DriverName, normalizedStoppedQuery)
                    || TextSearchMatcher.ContainsValue(item.NationalId, normalizedStoppedQuery)
                    || TextSearchMatcher.ContainsValue(item.DepartmentName, normalizedStoppedQuery)
                    || TextSearchMatcher.ContainsValue(
                        item.PlateNumberDisplay,
                        normalizedStoppedQuery
                    )
                    || TextSearchMatcher.ContainsValue(
                        item.ApprovalStatusDisplay,
                        normalizedStoppedQuery
                    )
                    || TextSearchMatcher.ContainsValue(item.StopReason, normalizedStoppedQuery)
                    || TextSearchMatcher.ContainsValue(item.StopSource, normalizedStoppedQuery)
                    || TextSearchMatcher.ContainsValue(item.RecordedBy, normalizedStoppedQuery)
                )
                .OrderByDescending(item => item.OccurredAt)
                .ThenBy(item => item.PermitNumber)
                .ToList();

            var normalizedPageSize = PageSizeNormalizer.NormalizePageSize(pageSize);
            var totalPages = Math.Max(
                1,
                (int)Math.Ceiling(stoppedPermits.Count / (double)normalizedPageSize)
            );
            var currentPage = Math.Min(Math.Max(page, 1), totalPages);
            var pagedStoppedPermits = stoppedPermits
                .Skip((currentPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();

            return new ReportsDashboardViewModel
            {
                ActiveSection = "StoppedPermits",
                ReportQuery = normalizedStoppedQuery,
                StoppedPermits = pagedStoppedPermits,
                StoppedPermitsCount = stoppedPermits.Count,
                StoppedPermitPage = currentPage,
                StoppedPermitPageSize = normalizedPageSize,
                StoppedPermitTotalPages = totalPages,
                StoppedPermitTotalCount = stoppedPermits.Count,
            };
        }

        public ReportsDashboardViewModel BuildVisitsDashboardModel(
            string visitQuickRange,
            DateTime? visitDayDate,
            DateTime? visitFromDate,
            DateTime? visitToDate,
            string visitStatusFilter,
            string? visitQuery,
            int page,
            int pageSize,
            string? currentUser
        )
        {
            var filter = BuildVisitFilterResult(
                visitQuickRange,
                visitDayDate,
                visitFromDate,
                visitToDate,
                visitStatusFilter,
                visitQuery,
                currentUser
            );
            var allVisits = _visitService
                .GetAllVisits()
                .OrderByDescending(v => v.VisitDate)
                .ToList();
            var now = _systemClock.LocalNow;
            var dayStart = now.Date;
            var weekStart = GetWeekStart(now);
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var normalizedPageSize = PageSizeNormalizer.NormalizePageSize(pageSize);
            var totalPages = Math.Max(
                1,
                (int)Math.Ceiling(filter.Visits.Count / (double)normalizedPageSize)
            );
            var currentPage = Math.Min(Math.Max(page, 1), totalPages);

            return new ReportsDashboardViewModel
            {
                ActiveSection = "Visits",
                VisitQuery = filter.Query,
                VisitQuickRange = filter.QuickRange,
                VisitQuickRangeDisplay = filter.QuickRangeDisplay,
                VisitStatusFilter = filter.StatusFilter,
                VisitStatusFilterDisplay = filter.StatusFilterDisplay,
                VisitFilterSummary = filter.Summary,
                VisitDayDate = filter.DayDate,
                VisitFromDate = filter.FromDate,
                VisitToDate = filter.ToDate,
                VisitPage = currentPage,
                VisitPageSize = normalizedPageSize,
                VisitTotalPages = totalPages,
                VisitTotalCount = filter.Visits.Count,
                Visits = filter
                    .Visits.Skip((currentPage - 1) * normalizedPageSize)
                    .Take(normalizedPageSize)
                    .ToList(),
                TotalVisits = filter.Visits.Count,
                PendingApprovalVisits = filter.Visits.Count(v => v.ApprovalStatus == "Pending"),
                AwaitingArrivalVisits = filter.Visits.Count(v =>
                    v.ApprovalStatus == "Approved" && v.Status == "Active"
                ),
                InsideVisits = filter.Visits.Count(v => v.Status == "Inside"),
                CompletedVisits = filter.Visits.Count(v => v.Status == "Completed"),
                VisitMetrics = new List<PeriodMetricViewModel>
                {
                    ReportMetricBuilder.BuildMetric(
                        "يومي",
                        allVisits.Count(v => v.VisitDate >= dayStart),
                        "الزيارات المسجلة اليوم"
                    ),
                    ReportMetricBuilder.BuildMetric(
                        "أسبوعي",
                        allVisits.Count(v => v.VisitDate >= weekStart),
                        "الزيارات المسجلة من بداية الأسبوع"
                    ),
                    ReportMetricBuilder.BuildMetric(
                        "شهري",
                        allVisits.Count(v => v.VisitDate >= monthStart),
                        "الزيارات المسجلة من بداية الشهر"
                    ),
                },
            };
        }

        public PermitReportResult BuildPermitReportResult(
            string permitTypeFilter,
            string? reportQuery,
            string? currentUser
        )
        {
            var normalizedPermitTypeFilter = ReportFilterNormalizer.NormalizePermitTypeFilter(
                permitTypeFilter
            );
            var normalizedReportQuery = reportQuery?.Trim() ?? string.Empty;
            var filteredPermits = ApplyPermitTypeFilter(
                    _permitService.GetAllPermits(currentUser),
                    normalizedPermitTypeFilter
                )
                .ToList();
            var permits = string.IsNullOrWhiteSpace(normalizedReportQuery)
                ? filteredPermits
                : FindMatchingPermits(filteredPermits, normalizedReportQuery);

            return new PermitReportResult
            {
                PermitTypeFilter = normalizedPermitTypeFilter,
                PermitTypeFilterDisplay = ReportDisplayFormatter.GetPermitTypeFilterDisplay(
                    normalizedPermitTypeFilter
                ),
                ReportQuery = normalizedReportQuery,
                Permits = permits,
            };
        }

        public PermitActivityReportResult BuildPermitActivityReportResult(
            string? activityQuery,
            string? currentUser
        )
        {
            var normalizedActivityQuery = activityQuery?.Trim() ?? string.Empty;
            var allVisiblePermits = _permitService.GetAllPermits(currentUser).ToList();
            var recentActivities = _permitService.GetRecentPermitActivities(200, currentUser);
            var activitiesSource = string.IsNullOrWhiteSpace(normalizedActivityQuery)
                ? recentActivities
                : BuildPermitActivitySearchSource(
                    recentActivities,
                    allVisiblePermits,
                    normalizedActivityQuery,
                    currentUser
                );
            var activities = FindMatchingPermitActivities(activitiesSource, normalizedActivityQuery)
                .OrderByDescending(item => item.OccurredAt)
                .ThenByDescending(item => item.Id)
                .ToList();

            return new PermitActivityReportResult
            {
                ActivityQuery = normalizedActivityQuery,
                Activities = activities,
                PermitsLookup = allVisiblePermits
                    .GroupBy(permit => permit.PermitNumber)
                    .ToDictionary(group => group.Key, group => group.First()),
            };
        }

        public PendingPermitsReportResult BuildPendingPermitsReportResult(string? currentUser)
        {
            return new PendingPermitsReportResult
            {
                Permits = _permitService.GetPendingPermits(currentUser).ToList(),
            };
        }

        public NotificationCenterViewModel BuildNotificationCenterModel(
            int maxItemsPerSection,
            string? currentUser
        )
        {
            var normalizedMaxItems = Math.Clamp(maxItemsPerSection, 1, 10);
            var allPermits = _permitService.GetAllPermits(currentUser).ToList();
            var permitsLookup = allPermits.ToDictionary(
                permit => permit.PermitNumber,
                StringComparer.OrdinalIgnoreCase
            );
            var workflowItems = UnauthorizedExitWorkflowReportBuilder
                .BuildUnauthorizedExitWorkflowItems(
                    _permitService.GetSequencedPermitActivities(currentUser).ToList(),
                    permitsLookup
                )
                .OrderByDescending(item => item.LastUpdatedAt ?? DateTime.MinValue)
                .ThenBy(item => item.PermitNumber)
                .ToList();
            var pendingPermits = allPermits
                .Where(permit =>
                    string.Equals(
                        permit.ApprovalStatus,
                        "Pending",
                        StringComparison.OrdinalIgnoreCase
                    ) && !permit.ArchivedAt.HasValue
                )
                .ToList();

            var sections = new List<NotificationSectionViewModel>
            {
                new()
                {
                    Key = "PendingWorkflow",
                    Title = "حالات الخروج المعلّقة",
                    Description = "محاولات خروج غير محسومة ما زالت تنتظر الحسم الأول.",
                    EmptyMessage = "لا توجد حالات خروج معلّقة حاليًا.",
                    ViewAllLabel = "فتح الحالات المعلّقة",
                    Count = workflowItems.Count(item =>
                        string.Equals(
                            item.WorkflowStatus,
                            "Pending",
                            StringComparison.OrdinalIgnoreCase
                        )
                    ),
                    Items = workflowItems
                        .Where(item =>
                            string.Equals(
                                item.WorkflowStatus,
                                "Pending",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .Take(normalizedMaxItems)
                        .Select(item => new NotificationItemViewModel
                        {
                            ReferenceId = item.SequenceId,
                            PermitNumber = item.PermitNumber,
                            DriverName = item.DriverName,
                            Subtitle = $"{item.PermitNumber} - {item.WorkflowStatusDisplay}",
                            Summary = item.SummaryMessage,
                            SequenceId = item.SequenceId,
                        })
                        .ToList(),
                },
                new()
                {
                    Key = "UnderReview",
                    Title = "حالات تحت المراجعة",
                    Description = "حالات تحتاج قرارًا إداريًا قبل الإغلاق أو التصعيد.",
                    EmptyMessage = "لا توجد حالات جديدة تحت المراجعة حاليًا.",
                    ViewAllLabel = "فتح حالات المراجعة",
                    Count = workflowItems.Count(item =>
                        string.Equals(
                            item.WorkflowStatus,
                            "UnderReview",
                            StringComparison.OrdinalIgnoreCase
                        )
                    ),
                    Items = workflowItems
                        .Where(item =>
                            string.Equals(
                                item.WorkflowStatus,
                                "UnderReview",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .Take(normalizedMaxItems)
                        .Select(item => new NotificationItemViewModel
                        {
                            ReferenceId = item.SequenceId,
                            PermitNumber = item.PermitNumber,
                            DriverName = item.DriverName,
                            Subtitle = $"{item.PermitNumber} - {item.WorkflowStatusDisplay}",
                            Summary = item.SummaryMessage,
                            SequenceId = item.SequenceId,
                        })
                        .ToList(),
                },
                new()
                {
                    Key = "StoppedPermits",
                    Title = "التصاريح الموقوفة",
                    Description = "تصاريح وصلت إلى الإيقاف وتحتاج متابعة تشغيلية.",
                    EmptyMessage = "لا توجد تصاريح موقوفة حاليًا.",
                    ViewAllLabel = "عرض التصاريح الموقوفة",
                    Count = allPermits.Count(permit =>
                        string.Equals(
                            permit.ApprovalStatus,
                            "Stopped",
                            StringComparison.OrdinalIgnoreCase
                        )
                    ),
                    Items = allPermits
                        .Where(permit =>
                            string.Equals(
                                permit.ApprovalStatus,
                                "Stopped",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .OrderByDescending(permit =>
                            permit.ArchivedAt ?? permit.ExpiresAt ?? DateTime.MinValue
                        )
                        .Take(normalizedMaxItems)
                        .Select(permit => new NotificationItemViewModel
                        {
                            ReferenceId = permit.PermitNumber,
                            PermitNumber = permit.PermitNumber,
                            DriverName = permit.DriverName,
                            Subtitle = $"{permit.PermitNumber} - {permit.ApprovalStatusDisplay}",
                            Summary = permit.LeaveReason,
                        })
                        .ToList(),
                },
                new()
                {
                    Key = "PendingPermits",
                    Title = "طلبات الاعتماد",
                    Description = "تصاريح بانتظار اعتماد أو تحويل الجهة المخولة.",
                    EmptyMessage = "لا توجد تصاريح معلقة حاليًا.",
                    ViewAllLabel = "عرض طلبات التصاريح",
                    Count = pendingPermits.Count,
                    Items = pendingPermits
                        .OrderByDescending(permit => permit.ExpiresAt ?? DateTime.MinValue)
                        .Take(normalizedMaxItems)
                        .Select(permit => new NotificationItemViewModel
                        {
                            ReferenceId = permit.PermitNumber,
                            PermitNumber = permit.PermitNumber,
                            DriverName = permit.DriverName,
                            Subtitle = $"{permit.PermitNumber} - {permit.PermitTypeDisplay}",
                            Summary = permit.DepartmentName,
                        })
                        .ToList(),
                },
            };

            return new NotificationCenterViewModel
            {
                TotalCount = sections.Sum(section => section.Count),
                Sections = sections,
            };
        }

        public PermitDetailedReportResult? BuildPermitDetailedReportResult(
            string permitNumber,
            string? currentUser
        )
        {
            var permit = _permitService.GetPermitByNumber(permitNumber, currentUser);
            if (permit == null)
            {
                return null;
            }

            return new PermitDetailedReportResult
            {
                Permit = permit,
                Activities = _permitService.GetPermitActivities(permitNumber, currentUser).ToList(),
            };
        }

        public VisitFilterResult BuildVisitFilterResult(
            string visitQuickRange,
            DateTime? visitDayDate,
            DateTime? visitFromDate,
            DateTime? visitToDate,
            string visitStatusFilter,
            string? visitQuery,
            string? currentUser
        )
        {
            var normalizedQuickRange = ReportFilterNormalizer.NormalizeVisitQuickRange(
                visitQuickRange
            );
            var normalizedStatusFilter = ReportFilterNormalizer.NormalizeVisitStatusFilter(
                visitStatusFilter
            );
            var normalizedQuery = visitQuery?.Trim() ?? string.Empty;
            var normalizedDayDate = ReportFilterNormalizer.NormalizeReportInputDate(visitDayDate);
            var normalizedFromDate = ReportFilterNormalizer.NormalizeReportInputDate(visitFromDate);
            var normalizedToDate = ReportFilterNormalizer.NormalizeReportInputDate(visitToDate);
            var visits = _visitService.GetAllVisits().AsEnumerable();
            var fileSuffix = new StringBuilder(normalizedQuickRange);

            visits = ApplyVisitDateFilter(
                visits,
                normalizedQuickRange,
                ref normalizedDayDate,
                ref normalizedFromDate,
                ref normalizedToDate,
                fileSuffix
            );
            visits = ApplyVisitStatusFilter(visits, normalizedStatusFilter);

            var summaryParts = new List<string>
            {
                $"الفترة: {ReportDisplayFormatter.GetVisitQuickRangeDisplay(normalizedQuickRange, normalizedDayDate, normalizedFromDate, normalizedToDate)}",
                $"الحالة: {ReportDisplayFormatter.GetVisitStatusFilterDisplay(normalizedStatusFilter)}",
            };

            if (!string.IsNullOrWhiteSpace(normalizedQuery))
            {
                visits = visits.Where(v =>
                    TextSearchMatcher.ContainsValue(v.VisitId, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(v.VisitorName, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(v.NationalId, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(v.Purpose, normalizedQuery)
                    || TextSearchMatcher.ContainsValue(v.SubjectDisplay, normalizedQuery)
                    || v.ActiveCompanions.Any(c =>
                        TextSearchMatcher.ContainsValue(c.FullName, normalizedQuery)
                        || TextSearchMatcher.ContainsValue(c.NationalId, normalizedQuery)
                        || TextSearchMatcher.ContainsValue(c.PhoneNumber, normalizedQuery)
                    )
                );
                summaryParts.Add($"البحث: {normalizedQuery}");
                fileSuffix.Append("_Search");
            }

            var currentUserAccount = _userAdminService.GetUserAccount(currentUser ?? string.Empty);
            visits = _accessControl.FilterVisitsForUser(visits, currentUserAccount).AsEnumerable();

            return new VisitFilterResult
            {
                Visits = visits.OrderByDescending(v => v.VisitDate).ToList(),
                QuickRange = normalizedQuickRange,
                QuickRangeDisplay = ReportDisplayFormatter.GetVisitQuickRangeDisplay(
                    normalizedQuickRange,
                    normalizedDayDate,
                    normalizedFromDate,
                    normalizedToDate
                ),
                StatusFilter = normalizedStatusFilter,
                StatusFilterDisplay = ReportDisplayFormatter.GetVisitStatusFilterDisplay(
                    normalizedStatusFilter
                ),
                DayDate = normalizedDayDate,
                FromDate = normalizedFromDate,
                ToDate = normalizedToDate,
                Query = normalizedQuery,
                Summary = string.Join(" | ", summaryParts),
                FileSuffix = fileSuffix.ToString(),
            };
        }

        private StoppedPermitViewModel MapStoppedPermit(Permit permit, string? currentUser)
        {
            var stopActivity = _permitService
                .GetPermitActivities(permit.PermitNumber, currentUser)
                .FirstOrDefault(activity =>
                    string.Equals(
                        activity.ActionType,
                        "Stopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        activity.ActionType,
                        "PermitStopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        activity.ActionType,
                        "UnauthorizedExitStopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        activity.ActionType,
                        "LeaveOverdueStopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        activity.ReasonCode,
                        "Stopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        activity.ReasonCode,
                        "UnauthorizedExitStopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        activity.ReasonCode,
                        "LeaveOverdueStopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            return new StoppedPermitViewModel
            {
                PermitNumber = permit.PermitNumber,
                DriverName = permit.DriverName,
                NationalId = permit.NationalId,
                DepartmentName = permit.DepartmentName,
                PlateNumberDisplay = permit.PlateNumberDisplay,
                ApprovalStatusDisplay = permit.ApprovalStatusDisplay,
                StopReason = stopActivity?.Message ?? permit.LeaveReason ?? "تم إيقاف التصريح.",
                StopSource = stopActivity?.Source ?? "النظام",
                RecordedBy = stopActivity?.RecordedBy ?? "-",
                OccurredAt =
                    stopActivity?.OccurredAt
                    ?? permit.ArchivedAt
                    ?? permit.ExpiresAt
                    ?? _systemClock.LocalNow,
            };
        }

        private IEnumerable<Visit> ApplyVisitDateFilter(
            IEnumerable<Visit> visits,
            string quickRange,
            ref DateTime? visitDayDate,
            ref DateTime? visitFromDate,
            ref DateTime? visitToDate,
            StringBuilder fileSuffix
        )
        {
            var now = _systemClock.LocalNow;
            var dayStart = now.Date;
            var weekStart = GetWeekStart(now);
            var monthStart = new DateTime(now.Year, now.Month, 1);

            switch (quickRange)
            {
                case "Daily":
                    fileSuffix
                        .Append('_')
                        .Append(dayStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                    return visits.Where(v => v.VisitDate >= dayStart);
                case "Weekly":
                    fileSuffix
                        .Append('_')
                        .Append(weekStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                    return visits.Where(v => v.VisitDate >= weekStart);
                case "Monthly":
                    fileSuffix
                        .Append('_')
                        .Append(monthStart.ToString("yyyyMM", CultureInfo.InvariantCulture));
                    return visits.Where(v => v.VisitDate >= monthStart);
                case "SpecificDay":
                    visitDayDate ??= dayStart;
                    fileSuffix
                        .Append('_')
                        .Append(
                            visitDayDate.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                        );
                    var selectedDay = visitDayDate.Value;
                    return visits.Where(v =>
                        v.VisitDate >= selectedDay && v.VisitDate < selectedDay.AddDays(1)
                    );
                case "DateRange":
                    if (!visitFromDate.HasValue && !visitToDate.HasValue)
                    {
                        visitFromDate = weekStart;
                        visitToDate = now.Date;
                    }

                    if (!visitFromDate.HasValue)
                    {
                        visitFromDate = visitToDate;
                    }

                    if (!visitToDate.HasValue)
                    {
                        visitToDate = visitFromDate;
                    }

                    if (visitFromDate > visitToDate)
                    {
                        (visitFromDate, visitToDate) = (visitToDate, visitFromDate);
                    }

                    var normalizedFromDate = visitFromDate.GetValueOrDefault();
                    var normalizedToDate = visitToDate.GetValueOrDefault();

                    fileSuffix
                        .Append('_')
                        .Append(
                            normalizedFromDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                        )
                        .Append('-')
                        .Append(
                            normalizedToDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                        );
                    var rangeStart = normalizedFromDate;
                    var rangeEnd = normalizedToDate;
                    return visits.Where(v =>
                        v.VisitDate >= rangeStart && v.VisitDate < rangeEnd.AddDays(1)
                    );
                default:
                    return visits;
            }
        }

        private static IEnumerable<Visit> ApplyVisitStatusFilter(
            IEnumerable<Visit> visits,
            string statusFilter
        )
        {
            return statusFilter switch
            {
                "PendingApproval" => visits.Where(v =>
                    string.Equals(v.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                ),
                "AwaitingArrival" => visits.Where(v =>
                    string.Equals(v.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(v.Status, "Active", StringComparison.OrdinalIgnoreCase)
                ),
                "Inside" => visits.Where(v =>
                    string.Equals(v.Status, "Inside", StringComparison.OrdinalIgnoreCase)
                ),
                "Completed" => visits.Where(v =>
                    string.Equals(v.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                ),
                "Rejected" => visits.Where(v =>
                    string.Equals(v.ApprovalStatus, "Rejected", StringComparison.OrdinalIgnoreCase)
                ),
                "Suspended" => visits.Where(v =>
                    string.Equals(v.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                ),
                _ => visits,
            };
        }

        private static DateTime GetWeekStart(DateTime current)
        {
            var daysSinceSaturday = ((int)current.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
            return current.Date.AddDays(-daysSinceSaturday);
        }

        private static List<Permit> FindMatchingPermits(
            IEnumerable<Permit> permits,
            string reportQuery
        )
        {
            if (string.IsNullOrWhiteSpace(reportQuery))
            {
                return new List<Permit>();
            }

            return permits
                .Where(p =>
                    TextSearchMatcher.ContainsValue(p.PermitNumber, reportQuery)
                    || TextSearchMatcher.ContainsValue(p.DriverName, reportQuery)
                    || TextSearchMatcher.ContainsValue(p.NationalId, reportQuery)
                    || TextSearchMatcher.ContainsValue(p.DepartmentName, reportQuery)
                    || TextSearchMatcher.ContainsValue(p.PlateNumber, reportQuery)
                )
                .OrderBy(p => p.DriverName)
                .ThenBy(p => p.PermitNumber)
                .ToList();
        }

        private IEnumerable<PermitActivity> BuildPermitActivitySearchSource(
            IEnumerable<PermitActivity> recentActivities,
            IReadOnlyCollection<Permit> visiblePermits,
            string activityQuery,
            string? currentUser
        )
        {
            var matchingPermitActivities = FindMatchingPermits(visiblePermits, activityQuery)
                .SelectMany(permit =>
                    _permitService.GetPermitActivities(permit.PermitNumber, currentUser)
                );

            return recentActivities
                .Concat(matchingPermitActivities)
                .GroupBy(activity => activity.Id)
                .Select(group => group.First());
        }

        private static IEnumerable<PermitActivity> FindMatchingPermitActivities(
            IEnumerable<PermitActivity> activities,
            string activityQuery
        )
        {
            if (string.IsNullOrWhiteSpace(activityQuery))
            {
                return activities;
            }

            return activities.Where(item =>
                TextSearchMatcher.ContainsValue(item.PermitNumber, activityQuery)
                || TextSearchMatcher.ContainsValue(item.DriverName, activityQuery)
                || TextSearchMatcher.ContainsValue(item.ActionLabel, activityQuery)
                || TextSearchMatcher.ContainsValue(item.Message, activityQuery)
                || TextSearchMatcher.ContainsValue(item.Source, activityQuery)
                || TextSearchMatcher.ContainsValue(item.SequenceId, activityQuery)
            );
        }

        private static Permit? ResolveSelectedPermit(
            IEnumerable<Permit> allPermits,
            IReadOnlyCollection<Permit> matchingPermits,
            string selectedPermitNumber
        )
        {
            if (!string.IsNullOrWhiteSpace(selectedPermitNumber))
            {
                return allPermits.FirstOrDefault(p => p.PermitNumber == selectedPermitNumber);
            }

            return matchingPermits.Count == 1 ? matchingPermits.First() : null;
        }

        private static IEnumerable<Permit> ApplyPermitTypeFilter(
            IEnumerable<Permit> permits,
            string permitTypeFilter
        )
        {
            return permitTypeFilter switch
            {
                Permit.PermitTypePermanent => permits.Where(p =>
                    p.PermitType == Permit.PermitTypePermanent
                ),
                Permit.PermitTypeExit => permits.Where(p => p.PermitType == Permit.PermitTypeExit),
                Permit.PermitTypeTemporary => permits.Where(p =>
                    Permit.IsVisitorPermitType(p.PermitType)
                ),
                Permit.PermitTypeGuest => permits.Where(p =>
                    Permit.IsVisitorPermitType(p.PermitType)
                ),
                Permit.PermitTypeVisitor => permits.Where(p =>
                    Permit.IsVisitorPermitType(p.PermitType)
                ),
                _ => permits,
            };
        }
    }
}
