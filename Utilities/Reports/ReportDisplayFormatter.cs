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
    public static class ReportDisplayFormatter
    {
        public static string GetPermitTypeFilterDisplay(string permitTypeFilter)
        {
            return permitTypeFilter switch
            {
                Permit.PermitTypePermanent => "دائم",
                Permit.PermitTypeExit => "خروج",
                Permit.PermitTypeTemporary => "زائر",
                Permit.PermitTypeGuest => "زائر",
                Permit.PermitTypeVisitor => "زائر",
                _ => "الكل",
            };
        }

        public static string GetVisitQuickRangeDisplay(
            string quickRange,
            DateTime? dayDate,
            DateTime? fromDate,
            DateTime? toDate
        )
        {
            return quickRange switch
            {
                "Daily" => "اليوم الحالي",
                "Weekly" => "الأسبوع الحالي",
                "Monthly" => "الشهر الحالي",
                "SpecificDay" => $"يوم محدد: {FormatReportDate(dayDate)}",
                "DateRange" => $"من {FormatReportDate(fromDate)} إلى {FormatReportDate(toDate)}",
                _ => "كل الفترات",
            };
        }

        public static string GetVisitStatusFilterDisplay(string statusFilter)
        {
            return statusFilter switch
            {
                "PendingApproval" => "بانتظار الاعتماد",
                "AwaitingArrival" => "بانتظار الوصول",
                "Inside" => "داخل الزيارة",
                "Completed" => "منتهية",
                "Rejected" => "مرفوضة",
                "Suspended" => "موقوفة",
                _ => "كل الحالات",
            };
        }

        public static string GetUnauthorizedExitWorkflowStatusDisplay(string statusFilter)
        {
            return statusFilter switch
            {
                "Pending" => "حالات معلقة",
                "UnderReview" => "تحت المراجعة",
                "Stopped" => "موقوفة",
                "Closed" => "مغلقة",
                _ => "كل الحالات",
            };
        }

        public static string FormatReportDate(DateTime? value)
        {
            return HijriDateFormatter.FormatWithDay(value);
        }
    }
}
