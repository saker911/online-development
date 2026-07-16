using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Reports;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Backup;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Delegations;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ViewDashboard)]
    public class HomeController : Controller
    {
        private readonly IPermitService _permitService;
        private readonly IVisitService _visitService;
        private readonly IReportsDashboardService _reportsDashboardService;
        private readonly IUserAdminService _userAdminService;

        public HomeController(
            IPermitService permitService,
            IVisitService visitService,
            IReportsDashboardService reportsDashboardService,
            IUserAdminService userAdminService
        )
        {
            _permitService = permitService;
            _visitService = visitService;
            _reportsDashboardService = reportsDashboardService;
            _userAdminService = userAdminService;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            if (!(User?.Identity?.IsAuthenticated ?? false))
            {
                return View("Landing");
            }

            var username = User.Identity?.Name;
            var permitsTask = Task.Run(() => _permitService.GetVisiblePermits(username).ToList());
            var visitsTask = Task.Run(() => _visitService.GetAllVisits().ToList());
            var administrationTask = Task.Run(_userAdminService.GetAdministrationSettings);
            await Task.WhenAll(permitsTask, visitsTask, administrationTask);

            var permits = await permitsTask;
            var visits = await visitsTask;
            var activities = new List<PermitActivity>();
            ReportsDashboardViewModel? workflowDashboard = null;
            if (permits.Count > 0)
            {
                activities = _permitService.GetRecentPermitActivities(200).ToList();
                workflowDashboard =
                    _reportsDashboardService.BuildUnauthorizedExitWorkflowDashboardModel(
                        null,
                        "All",
                        1,
                        200,
                        username
                    );
            }
            var today = DateTime.Today;
            var administration = await administrationTask;
            var officialWorkDays = AdministrationWorkSchedule.ParseOfficialWorkDays(
                administration.OfficialWorkDaysCsv
            );
            var todayIsOfficialWorkDay = officialWorkDays.Contains(AppClock.LocalNow.DayOfWeek);
            var withinWorkHours = AdministrationWorkSchedule.IsWithinWorkHours(
                AppClock.LocalNow,
                administration
            );
            var pendingPermits = permits
                .Where(p =>
                    string.Equals(p.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                )
                .OrderByDescending(p => p.ExpiresAt)
                .Take(4)
                .ToList();

            ViewBag.TotalPermits = permits.Count;
            ViewBag.ActivePermits = permits.Count(p =>
                p.ApprovalStatus == "Approved" || p.ApprovalStatus == "Out"
            );
            ViewBag.PendingPermitCount = permits.Count(p =>
                string.Equals(p.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
            );
            ViewBag.PendingPermits = pendingPermits;
            ViewBag.TotalVisits = visits.Count;
            ViewBag.SuspendedVisits = visits.Count(v => v.Status == "Suspended");
            ViewBag.TodayViolations = activities.Count(a =>
                a.OccurredAt >= today
                && (
                    string.Equals(
                        a.ActionType,
                        "SecurityViolation",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        a.ActionType,
                        "NoReturnViolation",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
            ViewBag.TodayLateReturns = activities.Count(a =>
                a.OccurredAt >= today
                && string.Equals(a.ActionType, "LateReturn", StringComparison.OrdinalIgnoreCase)
            );
            ViewBag.StoppedPermits = permits.Count(p =>
                string.Equals(p.ApprovalStatus, "Stopped", StringComparison.OrdinalIgnoreCase)
            );
            ViewBag.WorkflowPendingCount =
                workflowDashboard?.UnauthorizedExitWorkflowPendingCount ?? 0;
            ViewBag.WorkflowUnderReviewCount =
                workflowDashboard?.UnauthorizedExitWorkflowUnderReviewCount ?? 0;
            ViewBag.WorkflowStoppedCount =
                workflowDashboard?.UnauthorizedExitWorkflowStoppedCount ?? 0;
            ViewBag.WorkflowClosedCount =
                workflowDashboard?.UnauthorizedExitWorkflowClosedCount ?? 0;
            ViewBag.WorkDayStatus = todayIsOfficialWorkDay ? "يوم دوام" : "خارج الدوام الرسمي";
            ViewBag.WorkDayStatusHint = withinWorkHours
                ? "النظام الآن داخل ساعات الدوام ويطبق سياسات المنع والتصعيد حسب الإعدادات الحالية."
                : "النظام الآن خارج ساعات الدوام ويكتفي بالتسجيل دون تصعيد مخالفات غير مستحقة.";
            ViewBag.LateReturnGraceLabel =
                $"حضور {administration.AttendanceGraceMinutes} د، انصراف {administration.WorkEndExitGraceMinutes} د، استئذان {administration.LateReturnGraceMinutes} د";
            ViewBag.WorkHoursLabel =
                $"{administration.WorkStartTime:HH\\:mm} - {administration.WorkEndTime:HH\\:mm}";
            ViewBag.OfficialWorkDaysLabel = string.Join(
                "، ",
                officialWorkDays.Select(GetArabicDayName)
            );
            return View();
        }

        private static string GetArabicDayName(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Saturday => "السبت",
                DayOfWeek.Sunday => "الأحد",
                DayOfWeek.Monday => "الاثنين",
                DayOfWeek.Tuesday => "الثلاثاء",
                DayOfWeek.Wednesday => "الأربعاء",
                DayOfWeek.Thursday => "الخميس",
                DayOfWeek.Friday => "الجمعة",
                _ => dayOfWeek.ToString(),
            };
        }

        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            this.ToastError("ليس لديك صلاحية لتنفيذ هذا الإجراء.");
            return View();
        }

        [AllowAnonymous]
        [Route("/Error")]
        public IActionResult Error()
        {
            return View();
        }
    }
}
