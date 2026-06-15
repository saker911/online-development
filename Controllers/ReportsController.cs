using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly IPermitService _permitService;
        private readonly IReportsDashboardService _reportsDashboardService;
        private readonly IReportsDocumentService _reportsDocumentService;
        private readonly IAccessControlService _accessControl;
        private readonly IUserAdminService _userAdminService;

        public ReportsController(
            IPermitService permitService,
            IReportsDashboardService reportsDashboardService,
            IReportsDocumentService reportsDocumentService,
            IAccessControlService accessControl,
            IUserAdminService userAdminService
        )
        {
            _permitService = permitService;
            _reportsDashboardService = reportsDashboardService;
            _reportsDocumentService = reportsDocumentService;
            _accessControl = accessControl;
            _userAdminService = userAdminService;
        }

        public IActionResult Index()
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            var canViewPermits = _accessControl.CanViewPermits(currentUser);
            var canViewVisits = _accessControl.CanViewVisits(currentUser);

            if (!canViewPermits && !canViewVisits)
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            return View(
                _reportsDashboardService.BuildReportsHomeModel(
                    canViewPermits,
                    canViewVisits,
                    User.Identity?.Name
                )
            );
        }

        [HttpGet]
        public IActionResult Permits(
            string permitTypeFilter = "All",
            string? reportQuery = null,
            string? selectedPermitNumber = null,
            int page = 1,
            int pageSize = 10
        )
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewPermits(currentUser))
            {
                return RedirectToAction("AccessDenied", "Home");
            }
            var model = _reportsDashboardService.BuildPermitsDashboardModel(
                permitTypeFilter,
                reportQuery,
                selectedPermitNumber,
                page,
                pageSize,
                User.Identity?.Name
            );

            model.ApprovablePermitNumbers = model
                .PendingApprovalPermits.Where(permit =>
                    _accessControl.CanApprovePermit(permit, currentUser)
                )
                .Select(permit => permit.PermitNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return View(model);
        }

        [HttpGet]
        public IActionResult PermitActivity(
            string? activityQuery = null,
            string? selectedSequenceId = null,
            int page = 1,
            int pageSize = 10
        )
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewPermits(currentUser))
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            return View(
                _reportsDashboardService.BuildPermitActivityDashboardModel(
                    activityQuery,
                    selectedSequenceId,
                    page,
                    pageSize,
                    User.Identity?.Name
                )
            );
        }

        [HttpGet]
        public IActionResult UnauthorizedExitWorkflow(
            string? workflowQuery = null,
            string workflowStatusFilter = "All",
            int page = 1,
            int pageSize = 10
        )
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewPermits(currentUser))
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            return View(
                _reportsDashboardService.BuildUnauthorizedExitWorkflowDashboardModel(
                    workflowQuery,
                    workflowStatusFilter,
                    page,
                    pageSize,
                    User.Identity?.Name
                )
            );
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ReviewUnauthorizedExits)]
        public IActionResult ResolveUnauthorizedExitReview(
            string sequenceId,
            string resolution,
            string? activityQuery = null,
            string? permitNumber = null,
            bool reactivatePermit = false,
            int pageSize = 10
        )
        {
            var confirmViolation = string.Equals(
                resolution,
                "confirm",
                StringComparison.OrdinalIgnoreCase
            );

            var resolved = _permitService.ResolveUnauthorizedExitReview(
                sequenceId,
                confirmViolation,
                User.Identity?.Name
            );

            if (resolved)
            {
                if (confirmViolation)
                {
                    if (reactivatePermit && !string.IsNullOrWhiteSpace(permitNumber))
                    {
                        var reactivated = _permitService.ReactivatePermit(
                            permitNumber,
                            User.Identity?.Name
                        );
                        if (reactivated)
                        {
                            this.ToastSuccess("تم اعتماد المخالفة وإعادة تفعيل التصريح بنجاح.");
                        }
                        else
                        {
                            this.ToastError(
                                "تم اعتماد المخالفة والتصريح ما زال موقوفًا لأن إعادة التفعيل تعذرت."
                            );
                        }
                    }
                    else
                    {
                        this.ToastSuccess("تم اعتماد المخالفة والتصريح ما زال موقوفًا");
                    }
                }
                else
                {
                    this.ToastSuccess("تم رفض المخالفة وإغلاق المراجعة فقط.");
                }
            }
            else
            {
                this.ToastError(confirmViolation ? "تعذر اعتماد المخالفة." : "تعذر رفض المخالفة.");
            }

            return RedirectToAction(
                nameof(PermitActivity),
                new
                {
                    activityQuery,
                    selectedSequenceId = sequenceId,
                    page = 1,
                    pageSize,
                }
            );
        }

        [HttpGet]
        public IActionResult StoppedPermits(
            string? stoppedQuery = null,
            int page = 1,
            int pageSize = 10
        )
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewPermits(currentUser))
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            return View(
                _reportsDashboardService.BuildStoppedPermitsDashboardModel(
                    stoppedQuery,
                    page,
                    pageSize,
                    User.Identity?.Name
                )
            );
        }

        [HttpGet]
        public IActionResult Visits(
            string visitQuickRange = "All",
            DateTime? visitDayDate = null,
            DateTime? visitFromDate = null,
            DateTime? visitToDate = null,
            string visitStatusFilter = "All",
            string? visitQuery = null,
            int page = 1,
            int pageSize = 10
        )
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewVisits(currentUser))
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            return View(
                _reportsDashboardService.BuildVisitsDashboardModel(
                    visitQuickRange,
                    visitDayDate,
                    visitFromDate,
                    visitToDate,
                    visitStatusFilter,
                    visitQuery,
                    page,
                    pageSize,
                    User.Identity?.Name
                )
            );
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ApprovePermits)]
        public IActionResult ApprovePendingPermit(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
                return NotFound();
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanApprovePermit(permit, currentUser))
                return Forbid();

            _permitService.UpdatePermitApprovalStatus(id, "Approved", User.Identity?.Name);
            return RedirectToAction(nameof(Permits));
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ApprovePermits)]
        public IActionResult RejectPendingPermit(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
                return NotFound();
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanApprovePermit(permit, currentUser))
                return Forbid();

            _permitService.UpdatePermitApprovalStatus(id, "Rejected", User.Identity?.Name);
            return RedirectToAction(nameof(Permits));
        }

        [HttpGet]
        public IActionResult PrintVisitsReport(
            string visitQuickRange = "All",
            DateTime? visitDayDate = null,
            DateTime? visitFromDate = null,
            DateTime? visitToDate = null,
            string visitStatusFilter = "All",
            string? visitQuery = null
        )
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewVisits(currentUser))
            {
                return Forbid();
            }

            var filter = _reportsDashboardService.BuildVisitFilterResult(
                visitQuickRange,
                visitDayDate,
                visitFromDate,
                visitToDate,
                visitStatusFilter,
                visitQuery,
                User.Identity?.Name
            );
            var report = _reportsDocumentService.BuildVisitsReport(filter);
            return File(report.Content, report.ContentType, report.FileName);
        }

        [HttpGet]
        public IActionResult PrintPermitActivityReport(string? activityQuery = null)
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewPermits(currentUser))
            {
                return Forbid();
            }

            var activityReport = _reportsDashboardService.BuildPermitActivityReportResult(
                activityQuery,
                User.Identity?.Name
            );
            var report = _reportsDocumentService.BuildPermitActivityReport(
                activityReport.ActivityQuery,
                activityReport.Activities,
                activityReport.PermitsLookup
            );
            return File(report.Content, report.ContentType, report.FileName);
        }

        [HttpGet]
        public IActionResult PrintPendingPermitsReport()
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewPermits(currentUser))
            {
                return Forbid();
            }

            var pendingPermits = _reportsDashboardService.BuildPendingPermitsReportResult(
                User.Identity?.Name
            );
            var report = _reportsDocumentService.BuildPendingPermitsReport(pendingPermits.Permits);
            return File(report.Content, report.ContentType, report.FileName);
        }

        [HttpGet]
        public IActionResult PrintPermitDetailedReport(string permitNumber)
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewPermits(currentUser))
            {
                return Forbid();
            }

            var permitReport = _reportsDashboardService.BuildPermitDetailedReportResult(
                permitNumber,
                User.Identity?.Name
            );
            if (permitReport == null)
            {
                return NotFound();
            }

            var report = _reportsDocumentService.BuildPermitDetailedReport(
                permitReport.Permit,
                permitReport.Activities
            );
            return File(report.Content, report.ContentType, report.FileName);
        }

        [HttpGet]
        public IActionResult PrintPermitsReport(
            string permitTypeFilter = "All",
            string? reportQuery = null
        )
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanViewPermits(currentUser))
            {
                return Forbid();
            }

            var permitReport = _reportsDashboardService.BuildPermitReportResult(
                permitTypeFilter,
                reportQuery,
                User.Identity?.Name
            );
            var report = _reportsDocumentService.BuildPermitsReport(permitReport);
            return File(report.Content, report.ContentType, report.FileName);
        }
    }
}
