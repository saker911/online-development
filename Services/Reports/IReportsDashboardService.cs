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
    public interface IReportsDashboardService
    {
        ReportsDashboardViewModel BuildReportsHomeModel(
            bool canViewPermits,
            bool canViewVisits,
            string? currentUser
        );

        ReportsDashboardViewModel BuildPermitsDashboardModel(
            string permitTypeFilter,
            string? reportQuery,
            string? selectedPermitNumber,
            int page,
            int pageSize,
            string? currentUser
        );

        ReportsDashboardViewModel BuildPermitActivityDashboardModel(
            string? activityQuery,
            string? selectedSequenceId,
            int page,
            int pageSize,
            string? currentUser
        );

        ReportsDashboardViewModel BuildUnauthorizedExitWorkflowDashboardModel(
            string? workflowQuery,
            string workflowStatusFilter,
            int page,
            int pageSize,
            string? currentUser
        );

        ReportsDashboardViewModel BuildStoppedPermitsDashboardModel(
            string? stoppedQuery,
            int page,
            int pageSize,
            string? currentUser
        );

        ReportsDashboardViewModel BuildVisitsDashboardModel(
            string visitQuickRange,
            DateTime? visitDayDate,
            DateTime? visitFromDate,
            DateTime? visitToDate,
            string visitStatusFilter,
            string? visitQuery,
            int page,
            int pageSize,
            string? currentUser
        );

        PermitReportResult BuildPermitReportResult(
            string permitTypeFilter,
            string? reportQuery,
            string? currentUser
        );

        PermitActivityReportResult BuildPermitActivityReportResult(
            string? activityQuery,
            string? currentUser
        );

        PendingPermitsReportResult BuildPendingPermitsReportResult(string? currentUser);

        NotificationCenterViewModel BuildNotificationCenterModel(
            int maxItemsPerSection,
            string? currentUser
        );

        PermitDetailedReportResult? BuildPermitDetailedReportResult(
            string permitNumber,
            string? currentUser
        );

        VisitFilterResult BuildVisitFilterResult(
            string visitQuickRange,
            DateTime? visitDayDate,
            DateTime? visitFromDate,
            DateTime? visitToDate,
            string visitStatusFilter,
            string? visitQuery,
            string? currentUser
        );
    }

    public sealed class PermitReportResult
    {
        public string PermitTypeFilter { get; set; } = "All";
        public string PermitTypeFilterDisplay { get; set; } = "الكل";
        public string ReportQuery { get; set; } = string.Empty;
        public List<Permit> Permits { get; set; } = new();
    }

    public sealed class PermitActivityReportResult
    {
        public string ActivityQuery { get; set; } = string.Empty;
        public List<PermitActivity> Activities { get; set; } = new();
        public Dictionary<string, Permit> PermitsLookup { get; set; } = new();
    }

    public sealed class PendingPermitsReportResult
    {
        public List<Permit> Permits { get; set; } = new();
    }

    public sealed class NotificationCenterViewModel
    {
        public int TotalCount { get; set; }
        public List<NotificationSectionViewModel> Sections { get; set; } = new();
    }

    public sealed class NotificationSectionViewModel
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string EmptyMessage { get; set; } = string.Empty;
        public string ViewAllLabel { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<NotificationItemViewModel> Items { get; set; } = new();
    }

    public sealed class NotificationItemViewModel
    {
        public string ReferenceId { get; set; } = string.Empty;
        public string PermitNumber { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string SequenceId { get; set; } = string.Empty;
    }

    public sealed class PermitDetailedReportResult
    {
        public Permit Permit { get; set; } = new();
        public List<PermitActivity> Activities { get; set; } = new();
    }

    public sealed class VisitFilterResult
    {
        public List<Visit> Visits { get; set; } = new();
        public string QuickRange { get; set; } = "All";
        public string QuickRangeDisplay { get; set; } = "كل الفترات";
        public string StatusFilter { get; set; } = "All";
        public string StatusFilterDisplay { get; set; } = "كل الحالات";
        public DateTime? DayDate { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string Query { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string FileSuffix { get; set; } = "All";
    }
}
