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
    public interface IReportsDocumentService
    {
        ReportDocumentResult BuildVisitsReport(VisitFilterResult filter);
        ReportDocumentResult BuildPermitActivityReport(
            string? activityQuery,
            IReadOnlyCollection<PermitActivity> activities,
            IReadOnlyDictionary<string, Permit> permitsLookup
        );
        ReportDocumentResult BuildPendingPermitsReport(IReadOnlyCollection<Permit> permits);
        ReportDocumentResult BuildPermitDetailedReport(
            Permit permit,
            IReadOnlyCollection<PermitActivity> activities
        );
        ReportDocumentResult BuildPermitsReport(PermitReportResult permitReport);
    }

    public sealed class ReportDocumentResult
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "application/pdf";
        public string FileName { get; set; } = string.Empty;
    }
}
