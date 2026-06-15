namespace VehiclePermitSystemWeb.Models.ViewModels.Reports
{
    public class ReportsDashboardViewModel
    {
        public string ActiveSection { get; set; } = string.Empty;
        public string ReportQuery { get; set; } = string.Empty;
        public string ActivityQuery { get; set; } = string.Empty;
        public string SelectedPermitNumber { get; set; } = string.Empty;
        public string SelectedSequenceId { get; set; } = string.Empty;
        public string VisitQuery { get; set; } = string.Empty;
        public string VisitQuickRange { get; set; } = "All";
        public string VisitQuickRangeDisplay { get; set; } = "كل الفترات";
        public string VisitStatusFilter { get; set; } = "All";
        public string VisitStatusFilterDisplay { get; set; } = "كل الحالات";
        public string VisitFilterSummary { get; set; } = string.Empty;
        public HashSet<string> ApprovablePermitNumbers { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public DateTime? VisitDayDate { get; set; }
        public DateTime? VisitFromDate { get; set; }
        public DateTime? VisitToDate { get; set; }
        public List<Permit> Permits { get; set; } = new();
        public List<Permit> MatchingPermits { get; set; } = new();
        public List<Permit> PermitHolders { get; set; } = new();
        public List<Permit> PendingApprovalPermits { get; set; } = new();
        public List<PermitActivity> RecentPermitActivities { get; set; } = new();
        public List<PermitActivity> FilteredPermitActivities { get; set; } = new();
        public List<PermitActivity> SelectedPermitActivities { get; set; } = new();
        public List<PermitActivity> SelectedSequenceActivities { get; set; } = new();
        public List<UnauthorizedExitReviewSequenceViewModel> UnauthorizedExitReviews { get; set; } =
            new();
        public Permit? SelectedPermit { get; set; }
        public UnauthorizedExitReviewSequenceViewModel? SelectedUnauthorizedExitReview { get; set; }
        public List<Visit> Visits { get; set; } = new();
        public List<PeriodMetricViewModel> ExitMetrics { get; set; } = new();
        public List<PeriodMetricViewModel> VisitMetrics { get; set; } = new();
        public string PermitTypeFilter { get; set; } = "All";
        public string PermitTypeFilterDisplay { get; set; } = "الكل";
        public int PermitPage { get; set; } = 1;
        public int PermitPageSize { get; set; } = 10;
        public int PermitTotalPages { get; set; } = 1;
        public int PermitTotalCount { get; set; }
        public int PermitActivityPage { get; set; } = 1;
        public int PermitActivityPageSize { get; set; } = 10;
        public int PermitActivityTotalPages { get; set; } = 1;
        public int PermitActivityTotalCount { get; set; }
        public int VisitPage { get; set; } = 1;
        public int VisitPageSize { get; set; } = 10;
        public int VisitTotalPages { get; set; } = 1;
        public int VisitTotalCount { get; set; }
        public int TotalPermits { get; set; }
        public int ApprovedPermits { get; set; }
        public int PendingPermits { get; set; }
        public int ExitPermits { get; set; }
        public int PermanentPermits { get; set; }
        public int VisitorPermits { get; set; }
        public int StoppedPermitsCount { get; set; }
        public int StoppedPermitPage { get; set; } = 1;
        public int StoppedPermitPageSize { get; set; } = 10;
        public int StoppedPermitTotalPages { get; set; } = 1;
        public int StoppedPermitTotalCount { get; set; }
        public int TotalPermitActivities { get; set; }
        public int TodayPermitActivities { get; set; }
        public int AttendanceEntryCount { get; set; }
        public int LateAttendanceCount { get; set; }
        public int CheckoutExitCount { get; set; }
        public int LateCheckoutCount { get; set; }
        public int AttendanceCommitmentPercent { get; set; }
        public int CheckoutCommitmentPercent { get; set; }
        public int UnauthorizedExitReviewCount { get; set; }
        public int SequencedPermitActivityCount { get; set; }
        public string UnauthorizedExitWorkflowQuery { get; set; } = string.Empty;
        public string UnauthorizedExitWorkflowStatusFilter { get; set; } = "All";
        public string UnauthorizedExitWorkflowStatusDisplay { get; set; } = "كل الحالات";
        public int UnauthorizedExitWorkflowPage { get; set; } = 1;
        public int UnauthorizedExitWorkflowPageSize { get; set; } = 10;
        public int UnauthorizedExitWorkflowTotalPages { get; set; } = 1;
        public int UnauthorizedExitWorkflowTotalCount { get; set; }
        public int UnauthorizedExitWorkflowPendingCount { get; set; }
        public int UnauthorizedExitWorkflowUnderReviewCount { get; set; }
        public int UnauthorizedExitWorkflowStoppedCount { get; set; }
        public int UnauthorizedExitWorkflowClosedCount { get; set; }
        public int TotalVisits { get; set; }
        public int PendingApprovalVisits { get; set; }
        public int AwaitingArrivalVisits { get; set; }
        public int InsideVisits { get; set; }
        public int CompletedVisits { get; set; }
        public List<StoppedPermitViewModel> StoppedPermits { get; set; } = new();
        public List<UnauthorizedExitWorkflowItemViewModel> UnauthorizedExitWorkflowItems { get; set; } =
            new();
    }

    public class UnauthorizedExitReviewSequenceViewModel
    {
        public string SequenceId { get; set; } = string.Empty;
        public string PermitNumber { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string ApprovalStatusDisplay { get; set; } = string.Empty;
        public string ClassificationStatus { get; set; } = string.Empty;
        public string GateName { get; set; } = string.Empty;
        public string OperatorDisplay { get; set; } = string.Empty;
        public string SummaryMessage { get; set; } = string.Empty;
        public DateTime? FirstAttemptAt { get; set; }
        public DateTime? ReviewRaisedAt { get; set; }
        public int ActivityCount { get; set; }
        public int WarningCount { get; set; }
    }

    public class UnauthorizedExitWorkflowItemViewModel
    {
        public string SequenceId { get; set; } = string.Empty;
        public string PermitNumber { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string ApprovalStatusDisplay { get; set; } = string.Empty;
        public string WorkflowStatus { get; set; } = string.Empty;
        public string WorkflowStatusDisplay { get; set; } = string.Empty;
        public string FinalClassificationStatus { get; set; } = string.Empty;
        public string FinalClassificationDisplay { get; set; } = string.Empty;
        public string GateName { get; set; } = string.Empty;
        public string OperatorDisplay { get; set; } = string.Empty;
        public string SummaryMessage { get; set; } = string.Empty;
        public DateTime? FirstAttemptAt { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public DateTime? ReviewRaisedAt { get; set; }
        public int ActivityCount { get; set; }
        public int WarningCount { get; set; }
        public bool CanResolveReview { get; set; }
        public bool IsStopped { get; set; }
        public bool IsClosed { get; set; }
    }

    public class PeriodMetricViewModel
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class StoppedPermitViewModel
    {
        public string PermitNumber { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string NationalId { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string PlateNumberDisplay { get; set; } = string.Empty;
        public string ApprovalStatusDisplay { get; set; } = string.Empty;
        public string StopReason { get; set; } = string.Empty;
        public string StopSource { get; set; } = string.Empty;
        public string RecordedBy { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }
    }
}
