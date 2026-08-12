using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
using VehiclePermitSystemWeb.Models.ViewModels.Workplace;
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
using VehiclePermitSystemWeb.Services.Workplace;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ViewDisplays)]
    public class DisplayController : Controller
    {
        [AllowAnonymous]
        public IActionResult Index()
        {
            var authorizationResult = EnsureDisplayPageAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            return RedirectToAction(nameof(Access));
        }

        private readonly IUserAdminService _userAdminService;
        private readonly IPermitService _permitService;
        private readonly IVisitService _visitService;
        private readonly IDisplayDeviceService _displayDeviceService;
        private readonly IWorkplaceDirectoryService? _workplaceDirectoryService;

        public DisplayController(
            IUserAdminService userAdminService,
            IPermitService permitService,
            IVisitService visitService,
            IDisplayDeviceService displayDeviceService,
            IWorkplaceDirectoryService? workplaceDirectoryService = null
        )
        {
            _userAdminService = userAdminService;
            _permitService = permitService;
            _visitService = visitService;
            _displayDeviceService = displayDeviceService;
            _workplaceDirectoryService = workplaceDirectoryService;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult KeyEntry(string? returnUrl = null)
        {
            return RedirectToAction(
                nameof(Register),
                new { returnUrl = GetSafeDisplayReturnUrl(returnUrl) }
            );
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult KeyEntry(string accessKey, string? returnUrl = null)
        {
            return RedirectToAction(
                nameof(Register),
                new { returnUrl = GetSafeDisplayReturnUrl(returnUrl) }
            );
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Register(string? returnUrl = null)
        {
            var safeReturnUrl = GetSafeDisplayReturnUrl(returnUrl);
            ViewData["HideShell"] = true;
            ViewData["BodyClass"] = "login-page-body";
            ViewBag.ReturnUrl = safeReturnUrl;
            if (_displayDeviceService.TryActivateApprovedRequest(HttpContext))
            {
                return Redirect(safeReturnUrl);
            }

            var device = _displayDeviceService.GetDeviceFromRequestCookie(HttpContext);
            if (device?.Status == DisplayDeviceStatuses.Disabled)
            {
                return View("DisplayDeviceDisabled");
            }
            if (device?.Status == DisplayDeviceStatuses.Rejected)
            {
                return View("DisplayDeviceRejected");
            }
            if (device?.Status == DisplayDeviceStatuses.Pending)
            {
                return View(
                    new DisplayDeviceRegistrationViewModel
                    {
                        Submitted = true,
                        Message = "تم إرسال طلب اعتماد الشاشة إلى الإدارة، بانتظار الموافقة.",
                    }
                );
            }

            return View(BuildDeviceRegistrationModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("display-registration")]
        public IActionResult Register(
            DisplayDeviceRegistrationViewModel model,
            string? returnUrl = null
        )
        {
            var safeReturnUrl = GetSafeDisplayReturnUrl(returnUrl);
            ViewData["HideShell"] = true;
            ViewData["BodyClass"] = "login-page-body";
            ViewBag.ReturnUrl = safeReturnUrl;
            if (!ModelState.IsValid)
            {
                model.Locations = GetGateLocations();
                return View(model);
            }

            var device = _displayDeviceService.RegisterRequest(model, HttpContext);
            if (device == null)
            {
                ModelState.AddModelError(string.Empty, "تعذر إرسال طلب اعتماد الشاشة.");
                model.Locations = GetGateLocations();
                return View(model);
            }

            model.Submitted = true;
            model.Message = "تم إرسال طلب اعتماد الشاشة إلى الإدارة، بانتظار الموافقة.";
            _displayDeviceService.SetRequestCookie(HttpContext, device.RequestCode);
            return View(model);
        }

        [AllowAnonymous]
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult ActivationStatus()
        {
            var activated = _displayDeviceService.TryActivateApprovedRequest(HttpContext);
            return Json(new { approved = activated });
        }

        [AllowAnonymous]
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("display-heartbeat")]
        public IActionResult Heartbeat(DisplayDeviceHeartbeatViewModel model)
        {
            return Json(_displayDeviceService.RecordHealth(HttpContext, model));
        }

        [AllowAnonymous]
        public IActionResult Access(string? target = null)
        {
            var authorizationResult = EnsureDisplayPageAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            ViewBag.GateUrl = BuildDisplayUrl(nameof(Gate));
            return View();
        }

        [AllowAnonymous]
        public IActionResult Gate(string? id = null, bool operatorMode = false)
        {
            var authorizationResult = EnsureDisplayPageAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var configuredModeRedirect = RedirectApprovedDeviceToConfiguredMode(nameof(Gate));
            if (configuredModeRedirect != null)
            {
                return configuredModeRedirect;
            }

            var isAccountScanner =
                User.Identity?.IsAuthenticated == true
                && User.HasPermission(AppPermissions.ScanOperations)
                && !operatorMode;
            var username = isAccountScanner ? User.Identity?.Name ?? string.Empty : string.Empty;
            var model = new ScanTestViewModel
            {
                ScannerUserName = username,
                RecentActivities = _permitService
                    .GetRecentPermitActivities(50, username)
                    .Where(activity =>
                        !isAccountScanner
                        || string.Equals(
                            activity.GateOperatorAccount,
                            username,
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            activity.RecordedBy,
                            username,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .Take(6)
                    .ToList(),
            };

            if (!string.IsNullOrWhiteSpace(id))
            {
                var requestedPermit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
                if (requestedPermit == null)
                {
                    model.SamplePermitNotice =
                        "تعذر العثور على التصريح المطلوب أو لا تملك صلاحية عرضه.";
                }
                else if (
                    !string.Equals(
                        requestedPermit.ApprovalStatus,
                        "Approved",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    model.SamplePermitNotice = "لا يمكن عرض باركود البوابة إلا بعد اعتماد التصريح.";
                }
                else
                {
                    model.SamplePermitNumber = requestedPermit.PermitNumber;
                    model.SamplePermitPublicCode = requestedPermit.PublicPermitCode;
                    model.SamplePermitDisplayName = requestedPermit.DriverName;
                    model.IsSpecificPermitBarcode = true;
                }
            }

            ViewBag.IsUnifiedDisplayMode = true;
            ViewBag.RequiresOperator = !isAccountScanner;
            ViewBag.ScannerDisplayName =
                User.Claims.FirstOrDefault(claim => claim.Type == "DisplayName")?.Value
                ?? username;
            ViewBag.HeartbeatEndpoint = Url.Action(nameof(Heartbeat));
            ViewBag.OperatorStatusEndpoint = Url.Action(nameof(GateOperatorStatus));
            ViewBag.SwitchOperatorEndpoint = Url.Action(nameof(SwitchOperator));
            ViewBag.SignOutOperatorEndpoint = Url.Action(nameof(SignOutOperator));
            ViewBag.ChangeOperatorPinEndpoint = Url.Action(nameof(ChangeOperatorPin));
            ViewData["HideShell"] = !isAccountScanner;
            ViewData["BodyClass"] = isAccountScanner
                ? "unified-gate-account-body"
                : "unified-gate-kiosk-body";

            return View("~/Views/ScanConsole/Index.cshtml", model);
        }

        [AllowAnonymous]
        public IActionResult WaitingBoard()
        {
            var authorizationResult = EnsureDisplayPageAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var configuredModeRedirect = RedirectApprovedDeviceToConfiguredMode(
                nameof(WaitingBoard)
            );
            if (configuredModeRedirect != null)
            {
                return configuredModeRedirect;
            }

            ViewData["HideShell"] = true;
            ViewData["BodyClass"] = "waiting-board-body";
            return View(BuildWaitingBoardViewModel());
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult WaitingBoardStatus()
        {
            var authorizationResult = EnsureDisplayApiAccess();
            return authorizationResult ?? Json(BuildWaitingBoardViewModel());
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult GateStatus()
        {
            var authorizationResult = EnsureDisplayApiAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var approvedEmployees = _permitService.GetApprovedPermitsForDisplay().Take(4).ToList();
            var employeesOut = _permitService.GetEmployeesOutForDisplay().Take(4).ToList();
            var recentActivities = _permitService.GetRecentPermitActivities(10).ToList();
            var latestActivity = _permitService.GetLatestPermitActivity();
            var deviceId = (Request.Query["deviceId"].ToString() ?? string.Empty).Trim();
            var activeOperator = _userAdminService.GetDisplayOperatorSession(
                ResolveDisplaySessionKey(deviceId)
            );
            return Json(
                new
                {
                    approvedEmployeesCount = approvedEmployees.Count,
                    employeesOutCount = employeesOut.Count,
                    recentActivitiesCount = recentActivities.Count,
                    latestActivityId = latestActivity?.Id ?? 0,
                    activeOperator = SerializeDisplayOperator(activeOperator),
                    approvedEmployees = approvedEmployees.Select(SerializePermitForGate),
                    employeesOut = employeesOut.Select(SerializePermitForGate),
                    recentActivities = recentActivities.Select(SerializePermitActivity),
                    activity = latestActivity == null
                        ? null
                        : SerializePermitActivity(latestActivity),
                }
            );
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult GateOperatorStatus(string deviceId)
        {
            var authorizationResult = EnsureDisplayApiAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            return Json(
                new
                {
                    operatorSession = SerializeDisplayOperator(
                        _userAdminService.GetDisplayOperatorSession(
                            ResolveDisplaySessionKey(deviceId)
                        )
                    ),
                }
            );
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("display-operator")]
        public IActionResult SwitchOperator([FromBody] DisplayOperatorSignInRequest request)
        {
            var authorizationResult = EnsureDisplayApiAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var result = _userAdminService.SwitchDisplayOperator(
                ResolveDisplaySessionKey(request.DeviceId),
                request.BadgeCode,
                request.Pin,
                User.Identity?.Name
            );
            return Json(
                new
                {
                    success = result.Success,
                    errorCode = result.ErrorCode,
                    message = result.Message,
                    requiresPinChange = result.RequiresPinChange,
                    previousOperator = SerializeDisplayOperator(result.PreviousOperator),
                    currentOperator = SerializeDisplayOperator(result.CurrentOperator),
                }
            );
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult SignOutOperator([FromBody] DisplayOperatorDeviceRequest request)
        {
            var authorizationResult = EnsureDisplayApiAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var success = _userAdminService.SignOutDisplayOperator(
                ResolveDisplaySessionKey(request.DeviceId),
                out var previousOperator
            );
            return Json(
                new
                {
                    success,
                    previousOperator = SerializeDisplayOperator(previousOperator),
                    message = success
                        ? "تم تسجيل خروج المشغل."
                        : "لا يوجد مشغل نشط على هذا الجهاز.",
                }
            );
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("display-operator")]
        public IActionResult ChangeOperatorPin([FromBody] DisplayOperatorPinChangeRequest request)
        {
            var authorizationResult = EnsureDisplayApiAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var success = _userAdminService.ChangeDisplayOperatorPin(
                ResolveDisplaySessionKey(request.DeviceId),
                request.CurrentPin,
                request.NewPin,
                out var errorCode
            );
            return Json(
                new
                {
                    success,
                    errorCode,
                    message = success ? "تم تحديث الرمز السري بنجاح."
                    : string.Equals(errorCode, "operator_pin_unchanged", StringComparison.Ordinal)
                        ? "يجب إدخال PIN جديد مختلف عن الرمز الحالي."
                    : "تعذر تحديث الرمز السري الحالي.",
                    operatorSession = SerializeDisplayOperator(
                        _userAdminService.GetDisplayOperatorSession(
                            ResolveDisplaySessionKey(request.DeviceId)
                        )
                    ),
                }
            );
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult SaveOperatorNote([FromBody] DisplayOperatorNoteRequest request)
        {
            var authorizationResult = EnsureDisplayApiAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var operatorSession = _userAdminService.GetDisplayOperatorSession(
                ResolveDisplaySessionKey(request.DeviceId)
            );
            if (operatorSession == null)
            {
                return Json(
                    new
                    {
                        success = false,
                        errorCode = "operator_not_signed_in",
                        message = "يجب تسجيل دخول مشغل قبل حفظ الملاحظة.",
                    }
                );
            }

            var success = _permitService.AddOperatorNote(
                request.PermitNumber,
                request.NoteText,
                operatorSession.Username,
                BuildDisplayAuditContext(operatorSession, "operator-note")
            );
            return Json(
                new
                {
                    success,
                    message = success
                        ? "تم حفظ الملاحظة في سجل الحركة."
                        : "تعذر حفظ الملاحظة على التصريح الحالي.",
                }
            );
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult EmployeeDeparted(string permitId, string? deviceId = null)
        {
            var authorizationResult = EnsureDisplayMutationAccess(
                deviceId,
                out var operatorSession
            );
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            _permitService.MarkPermitDeparted(
                permitId,
                operatorSession!.Username,
                BuildDisplayAuditContext(operatorSession, "display-manual-departure")
            );
            return RedirectToAction(nameof(Gate));
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult EmployeeReturned(string permitId, string? deviceId = null)
        {
            var authorizationResult = EnsureDisplayMutationAccess(
                deviceId,
                out var operatorSession
            );
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            _permitService.MarkPermitReturned(
                permitId,
                operatorSession!.Username,
                BuildDisplayAuditContext(operatorSession, "display-manual-return")
            );
            return RedirectToAction(nameof(Gate));
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult VisitorArrived(string visitId, string? deviceId = null)
        {
            var authorizationResult = EnsureDisplayMutationAccess(
                deviceId,
                out var operatorSession
            );
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            _visitService.MarkVisitorArrived(visitId, operatorSession!.Username);
            return RedirectToAction(nameof(Gate));
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult VisitorExited(string visitId, string? deviceId = null)
        {
            var authorizationResult = EnsureDisplayMutationAccess(
                deviceId,
                out var operatorSession
            );
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            _visitService.MarkVisitorExited(visitId, operatorSession!.Username);
            return RedirectToAction(nameof(Gate));
        }

        [AllowAnonymous]
        public IActionResult Visits()
        {
            var authorizationResult = EnsureDisplayPageAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            return RedirectToAction(nameof(Gate));
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult VisitsStatus()
        {
            return WaitingBoardStatus();
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult DetainedVisitorArrived(string visitId, string? deviceId = null)
        {
            var authorizationResult = EnsureDisplayMutationAccess(
                deviceId,
                out var operatorSession
            );
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            _visitService.MarkVisitorArrived(visitId, operatorSession!.Username);
            return RedirectToAction(nameof(Gate));
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult DetainedVisitorExited(string visitId, string? deviceId = null)
        {
            var authorizationResult = EnsureDisplayMutationAccess(
                deviceId,
                out var operatorSession
            );
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            _visitService.MarkVisitorExited(visitId, operatorSession!.Username);
            return RedirectToAction(nameof(Gate));
        }

        private string BuildDisplayUrl(string actionName)
        {
            var path = Url.Action(actionName, "Display") ?? $"/Display/{actionName}";
            var settings = _userAdminService.GetAdministrationSettings();
            var configuredBaseUrl = settings.DisplayBaseUrl;
            if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
            {
                return $"{configuredBaseUrl.TrimEnd('/')}{path}";
            }

            return Url.Action(actionName, "Display", values: null, protocol: Request.Scheme)
                ?? path;
        }

        private string GetSafeDisplayReturnUrl(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return returnUrl;
            }

            return Url.Action(nameof(Gate)) ?? "/Display/Gate";
        }

        private DisplayDeviceRegistrationViewModel BuildDeviceRegistrationModel() => new()
        {
            Locations = GetGateLocations(),
        };

        private IReadOnlyList<WorkplaceLocationOptionViewModel> GetGateLocations() =>
            _workplaceDirectoryService?.GetLocationOptions()
                .Where(item => item.GateEnabled)
                .ToList() ?? new List<WorkplaceLocationOptionViewModel>();

        private IActionResult? RedirectApprovedDeviceToConfiguredMode(string currentAction)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return null;
            }

            var device = _displayDeviceService.GetApprovedDevice(HttpContext);
            if (device == null)
            {
                return null;
            }

            var targetAction = DisplayDeviceModes.Normalize(device.Mode) switch
            {
                DisplayDeviceModes.WaitingBoard => nameof(WaitingBoard),
                _ => nameof(Gate),
            };
            return string.Equals(currentAction, targetAction, StringComparison.Ordinal)
                ? null
                : RedirectToAction(targetAction);
        }

        private DisplayWaitingBoardViewModel BuildWaitingBoardViewModel()
        {
            var queueVisits = _visitService.GetQueueVisits().ToList();
            return new DisplayWaitingBoardViewModel
            {
                Waiting = queueVisits
                    .Where(visit => visit.QueueStatus == Visit.QueueStatusWaiting)
                    .Take(8)
                    .Select(visit => BuildWaitingTicket(
                        visit,
                        "بانتظار الاستدعاء",
                        visit.QueuedAtUtc?.ToLocalTime() ?? visit.VisitDate
                    ))
                    .ToList(),
                Inside = queueVisits
                    .Where(visit => visit.QueueStatus == Visit.QueueStatusCalled)
                    .Take(8)
                    .Select(visit => BuildWaitingTicket(
                        visit,
                        "تم الاستدعاء",
                        visit.CalledAtUtc?.ToLocalTime() ?? visit.VisitDate
                    ))
                    .ToList(),
                Completed = queueVisits
                    .Where(visit => visit.QueueStatus == Visit.QueueStatusServing)
                    .Take(8)
                    .Select(visit => BuildWaitingTicket(
                        visit,
                        "قيد الخدمة",
                        visit.ServiceStartedAtUtc?.ToLocalTime() ?? visit.VisitDate
                    ))
                    .ToList(),
                UpdatedAtText = DateTime.Now.ToString("HH:mm:ss"),
            };
        }

        private static DisplayWaitingTicketViewModel BuildWaitingTicket(
            Visit visit,
            string statusText,
            DateTime time
        )
        {
            var location = (visit.VisitLocation ?? string.Empty).Trim();
            if (location.Length > 64)
            {
                location = location[..64] + "…";
            }

            return new DisplayWaitingTicketViewModel
            {
                TicketNumber = visit.QueueTicketNumber,
                Location = string.IsNullOrWhiteSpace(location) ? "الاستقبال" : location,
                TimeText = HijriDateFormatter.Format(time),
                StatusText = statusText,
            };
        }

        private IActionResult? EnsureDisplayPageAccess()
        {
            if (_displayDeviceService.TryActivateApprovedRequest(HttpContext))
            {
                return null;
            }

            if (HasDisplayAccess())
            {
                return null;
            }

            if (User.Identity?.IsAuthenticated == true)
            {
                return Forbid();
            }

            var device =
                ResolveDeviceFromCookie()
                ?? _displayDeviceService.GetDeviceFromRequestCookie(HttpContext);
            if (device?.Status == DisplayDeviceStatuses.Disabled)
            {
                return View("DisplayDeviceDisabled");
            }
            if (device?.Status == DisplayDeviceStatuses.Rejected)
            {
                return View("DisplayDeviceRejected");
            }

            return RedirectToAction(nameof(Register), new { returnUrl = Request.Path.ToString() });
        }

        private IActionResult? EnsureDisplayApiAccess()
        {
            if (HasDisplayAccess())
            {
                return null;
            }

            return Forbid();
        }

        private IActionResult? EnsureDisplayMutationAccess(
            string? deviceId,
            out DisplayOperatorSessionInfo? operatorSession
        )
        {
            operatorSession = null;
            var authorizationResult = EnsureDisplayPageAccess();
            if (authorizationResult != null)
            {
                return authorizationResult;
            }

            var normalizedDeviceId = ResolveDisplaySessionKey(ResolvePostedDeviceId(deviceId));
            if (string.IsNullOrWhiteSpace(normalizedDeviceId))
            {
                return BuildDisplayMutationDeniedResult();
            }

            operatorSession = _userAdminService.GetDisplayOperatorSession(normalizedDeviceId);
            if (operatorSession == null)
            {
                return BuildDisplayMutationDeniedResult();
            }

            return operatorSession.MustChangePin
                ? BuildDisplayMutationDeniedResult(
                    "operator_pin_change_required",
                    "يجب تغيير PIN المؤقت قبل تنفيذ عمليات البوابة."
                )
                : null;
        }

        private string ResolvePostedDeviceId(string? deviceId)
        {
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return deviceId.Trim();
            }

            if (Request.HasFormContentType)
            {
                var formDeviceId = Request.Form["deviceId"].ToString();
                if (!string.IsNullOrWhiteSpace(formDeviceId))
                {
                    return formDeviceId.Trim();
                }
            }

            return Request.Query["deviceId"].ToString().Trim();
        }

        private string ResolveDisplaySessionKey(string? clientDeviceId)
        {
            if (
                User.Identity?.IsAuthenticated == true
                && (
                    User.HasPermission(AppPermissions.ViewDisplays)
                    || User.HasPermission(AppPermissions.ScanOperations)
                )
            )
            {
                return $"user:{User.Identity.Name}:{clientDeviceId}";
            }

            var approvedDevice = _displayDeviceService.GetApprovedDevice(HttpContext);
            return approvedDevice == null ? string.Empty : $"display-device:{approvedDevice.Id}";
        }

        private IActionResult BuildDisplayMutationDeniedResult(
            string errorCode = "operator_not_signed_in",
            string message = "يجب تسجيل دخول مشغل بوابة فعّال قبل تنفيذ العملية."
        )
        {
            if (
                string.Equals(
                    Request.Headers.XRequestedWith,
                    "XMLHttpRequest",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        success = false,
                        errorCode,
                        message,
                    }
                );
            }

            return StatusCode(StatusCodes.Status403Forbidden, message);
        }

        private bool HasDisplayAccess()
        {
            if (
                User.Identity?.IsAuthenticated == true
                && (
                    User.HasPermission(AppPermissions.ViewDisplays)
                    || User.HasPermission(AppPermissions.ScanOperations)
                )
            )
            {
                return true;
            }

            return _displayDeviceService.GetApprovedDevice(HttpContext) != null;
        }

        private DisplayDevice? ResolveDeviceFromCookie()
        {
            return _displayDeviceService.GetDeviceFromCookie(HttpContext);
        }

        private static object SerializePermitForGate(Permit permit)
        {
            return new
            {
                permitNumber = permit.PermitNumber,
                driverName = permit.DriverName,
                permitTypeDisplay = permit.PermitTypeDisplay,
                employeeDepartment = permit.EmployeeDepartment,
                subject = permit.Subject,
                outTimeText = HijriDateFormatter.Format(permit.OutTime),
            };
        }

        private static object SerializePermitActivity(PermitActivity activity)
        {
            return new
            {
                id = activity.Id,
                permitNumber = activity.PermitNumber,
                driverName = activity.DriverName,
                departmentName = activity.DepartmentName,
                actionLabel = activity.ActionLabel,
                message = activity.Message,
                occurredAtText = HijriDateFormatter.Format(activity.OccurredAt),
                source = activity.Source,
                operatorDisplay = string.IsNullOrWhiteSpace(activity.GateOperatorName)
                    ? activity.RecordedBy
                : string.IsNullOrWhiteSpace(activity.GateOperatorAccount)
                    ? activity.GateOperatorName
                : $"{activity.GateOperatorName} ({activity.GateOperatorAccount})",
                statusText = BuildActivityStatusText(activity),
                theme = BuildActivityTheme(activity),
            };
        }

        private object? SerializeDisplayOperator(DisplayOperatorSessionInfo? session)
        {
            if (session == null)
            {
                return null;
            }

            return new
            {
                username = session.Username,
                displayName = session.DisplayName,
                badgeCode = session.BadgeCode,
                deviceId = session.DeviceId,
                mustChangePin = session.MustChangePin,
                signedInAtUtc = session.SignedInAtUtc,
                signedInAtText = HijriDateFormatter.Format(session.SignedInAtUtc.ToLocalTime()),
            };
        }

        private PermitScanAuditContext BuildDisplayAuditContext(
            DisplayOperatorSessionInfo session,
            string executionMethod
        )
        {
            return new PermitScanAuditContext
            {
                GateName = "شاشة البوابة",
                GateOperatorName = session.DisplayName,
                GateOperatorAccount = session.Username,
                DeviceId = session.DeviceId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                ExecutionMethod = executionMethod,
                IsAutomated = false,
            };
        }

        private static object SerializeVisitForDisplay(Visit visit)
        {
            return new
            {
                visitId = visit.VisitId,
                visitorName = visit.VisitorName,
                companionCount = visit.CompanionCount,
                companionSummary = visit.CompanionSummary,
                visitLocation = visit.VisitLocation,
                nationalId = MaskSensitiveValue(visit.NationalId, 4),
                phoneNumber = MaskSensitiveValue(visit.PhoneNumber, 4),
                purpose = visit.Purpose,
                hostName = visit.SubjectDisplay,
                visitedPersonName = visit.SubjectDisplay,
                visitedPersonType = visit.VisitedPersonTypeDisplay,
                visitDateText = HijriDateFormatter.Format(visit.VisitDate),
                entryTimeText = HijriDateFormatter.Format(visit.EntryTime),
                exitTimeText = HijriDateFormatter.Format(visit.ExitTime),
                status = visit.Status,
            };
        }

        private static string BuildActivityStatusText(PermitActivity activity)
        {
            return string.Equals(
                    activity.ActionType,
                    "Departure",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "يسمح بالخروج"
                : string.Equals(activity.ActionType, "Return", StringComparison.OrdinalIgnoreCase)
                    ? "تم السماح بالدخول"
                : string.Equals(
                    activity.ActionType,
                    "NoReturnWarning",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "تنبيه تأخر العودة"
                : activity.ActionLabel;
        }

        private static string MaskSensitiveValue(string? value, int visibleSuffixLength)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var suffixLength = Math.Min(Math.Max(visibleSuffixLength, 0), normalized.Length);
            return new string('*', normalized.Length - suffixLength) + normalized[^suffixLength..];
        }

        private static string BuildActivityTheme(PermitActivity activity)
        {
            return string.Equals(
                    activity.ActionType,
                    "Departure",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "success"
                : string.Equals(activity.ActionType, "Return", StringComparison.OrdinalIgnoreCase)
                    ? "info"
                : string.Equals(
                    activity.ActionType,
                    "NoReturnWarning",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "warning"
                : "danger";
        }
    }
}
