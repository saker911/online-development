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

namespace VehiclePermitSystemWeb.Utilities.Reports
{
    public static class ReportFilterNormalizer
    {
        public static string NormalizePermitTypeFilter(string? permitTypeFilter)
        {
            return permitTypeFilter switch
            {
                Permit.PermitTypePermanent => Permit.PermitTypePermanent,
                Permit.PermitTypeExit => Permit.PermitTypeExit,
                Permit.PermitTypeTemporary => Permit.PermitTypeVisitor,
                Permit.PermitTypeGuest => Permit.PermitTypeVisitor,
                Permit.PermitTypeVisitor => Permit.PermitTypeVisitor,
                _ => "All",
            };
        }

        public static string NormalizeUnauthorizedExitWorkflowStatusFilter(string? statusFilter)
        {
            return statusFilter switch
            {
                "Pending" => "Pending",
                "UnderReview" => "UnderReview",
                "Stopped" => "Stopped",
                "Closed" => "Closed",
                _ => "All",
            };
        }

        public static string NormalizeVisitQuickRange(string? quickRange)
        {
            return quickRange switch
            {
                "Daily" => "Daily",
                "Weekly" => "Weekly",
                "Monthly" => "Monthly",
                "SpecificDay" => "SpecificDay",
                "DateRange" => "DateRange",
                _ => "All",
            };
        }

        public static DateTime? NormalizeReportInputDate(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var normalized = value.Value.Date;
            return HijriDateFormatter.IsSupported(normalized) ? normalized : null;
        }

        public static string NormalizeVisitStatusFilter(string? statusFilter)
        {
            return statusFilter switch
            {
                "PendingApproval" => "PendingApproval",
                "AwaitingArrival" => "AwaitingArrival",
                "Inside" => "Inside",
                "Completed" => "Completed",
                "Rejected" => "Rejected",
                "Suspended" => "Suspended",
                _ => "All",
            };
        }
    }
}
