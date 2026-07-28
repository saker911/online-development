using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Utilities.Barcodes;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;
using VehiclePermitSystemWeb.Utilities.Online;
using ZXing;
using ZXing.Common;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ViewPermits)]
    public class PermitsController : Controller
    {
        private const string ZebraLabelPreset = "zebra";
        private const float ZebraLabelWidthMm = 100f;
        private const float ZebraLabelHeightMm = 50f;

        private sealed record ZebraLabelContent(string BarcodeValue, string PlateNumberDisplay);

        private readonly IPermitService _permitService;
        private readonly IUserAdminService _userAdminService;
        private readonly IAccessControlService _accessControl;
        private readonly IWebHostEnvironment _environment;
        private readonly ISystemClock _systemClock;
        private readonly IConfiguration _configuration;

        public PermitsController(
            IPermitService permitService,
            IUserAdminService userAdminService,
            IAccessControlService accessControl,
            IWebHostEnvironment environment,
            ISystemClock systemClock,
            IConfiguration configuration
        )
        {
            _permitService = permitService;
            _userAdminService = userAdminService;
            _accessControl = accessControl;
            _environment = environment;
            _systemClock = systemClock;
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Index(string searchTerm = "", int page = 1, int pageSize = 5)
        {
            var normalizedPageSize = PermitInputNormalizer.NormalizePageSize(pageSize);
            var hasSearchTerm = !string.IsNullOrWhiteSpace(searchTerm);
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            ViewData["LeaveRequestsEnabled"] = _userAdminService
                .GetAdministrationSettings()
                .LeaveRequestsEnabled;
            var allPermits = _permitService
                .GetAllPermits(User.Identity?.Name)
                .OrderByDescending(p => PermitInputNormalizer.ParsePermitNumber(p.PermitNumber))
                .AsEnumerable();
            var permits = allPermits;

            if (!hasSearchTerm)
            {
                permits = permits.Where(p => !p.ArchivedAt.HasValue);
            }

            if (hasSearchTerm)
            {
                var term = searchTerm.Trim();
                var normalizedTerm = PermitInputNormalizer.NormalizeSearchTerm(term);
                var comparableTerm = PermitInputNormalizer.NormalizeSearchComparableValue(term);
                var comparableNormalizedTerm = PermitInputNormalizer.NormalizeSearchComparableValue(
                    normalizedTerm
                );
                var comparablePlateTerm = PermitInputNormalizer.NormalizePlateNumber(term);
                permits = permits.Where(p =>
                    PermitInputNormalizer.ContainsSearchValue(p.DriverName, comparableTerm)
                    || PermitInputNormalizer.ContainsSearchValue(p.NationalId, comparableTerm)
                    || PermitInputNormalizer.ContainsSearchValue(p.PermitNumber, comparableTerm)
                    || PermitInputNormalizer.ContainsSearchValue(
                        p.PermitNumber,
                        comparableNormalizedTerm
                    )
                    || PermitInputNormalizer.ContainsSearchValue(p.PublicPermitCode, comparableTerm)
                    || PermitInputNormalizer.ContainsSearchValue(p.PlateNumber, comparableTerm)
                    || (
                        !string.IsNullOrWhiteSpace(comparablePlateTerm)
                        && PermitInputNormalizer
                            .NormalizePlateNumber(p.PlateNumber)
                            .Contains(comparablePlateTerm, StringComparison.OrdinalIgnoreCase)
                    )
                );
            }

            var totalCount = permits.Count();
            var totalPages = Math.Max(
                1,
                (int)Math.Ceiling(totalCount / (double)normalizedPageSize)
            );
            var currentPage = Math.Min(Math.Max(page, 1), totalPages);
            var pagePermits = permits
                .Skip((currentPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();
            var approvablePermitNumbers = pagePermits
                .Where(p =>
                    string.Equals(p.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                    && _accessControl.CanApprovePermit(p, currentUser)
                )
                .Select(p => p.PermitNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reviewablePermitNumbers = pagePermits
                .Where(p => CanForwardApproval(p, currentUser))
                .Select(p => p.PermitNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pendingApprovalCount = permits.Count(p =>
                string.Equals(p.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                && _accessControl.CanApprovePermit(p, currentUser)
            );

            var model = new PermitIndexViewModel
            {
                SearchTerm = searchTerm,
                ApprovablePermitNumbers = approvablePermitNumbers,
                ReviewablePermitNumbers = reviewablePermitNumbers,
                PendingApprovalCount = pendingApprovalCount,
                PendingReviewCount = permits.Count(p => CanForwardApproval(p, currentUser)),
                StoppedPermitsCount = allPermits.Count(p =>
                    string.Equals(p.ApprovalStatus, "Stopped", StringComparison.OrdinalIgnoreCase)
                ),
                CurrentPage = currentPage,
                TotalPages = totalPages,
                PageSize = normalizedPageSize,
                Permits = pagePermits,
            };

            return View(model);
        }

        [Authorize(Policy = AppPolicies.CreatePermits)]
        public IActionResult Create()
        {
            PopulateDepartmentOptions();
            var defaultPermitType = User.HasPermission(AppPermissions.CreatePermit)
                ? Permit.PermitTypePermanent
                : Permit.PermitTypeVisitor;
            return View(
                new Permit
                {
                    PermitType = defaultPermitType,
                    PermitDate = _systemClock.LocalNow,
                    ExpiresAt = null,
                }
            );
        }

        public IActionResult Details(string id, string? returnUrl = null, bool focusActions = false)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            var administration = _userAdminService.GetAdministrationSettings();
            ViewData["PermitNotice"] = BuildPermitUsageNotice(administration);
            ViewData["LeaveRequestsEnabled"] = administration.LeaveRequestsEnabled;
            ViewData["OrganizationName"] = administration.OrganizationName;
            ViewData["DepartmentName"] = administration.DepartmentName;
            if (
                string.Equals(
                    permit.ApprovalStatus,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                var passToken = PermitQrTokenGenerator.IsCurrentVersion(permit.QrToken)
                    ? permit.QrToken
                    : _permitService.EnsurePermitQrToken(
                        permit.PermitNumber,
                        User.Identity?.Name
                    );
                if (!string.IsNullOrWhiteSpace(passToken))
                {
                    ViewData["DigitalPassUrl"] = BuildPermitDigitalPassUrl(permit, passToken);
                }
            }
            ViewBag.CanForwardApproval = CanForwardApproval(permit);
            ViewData["CanApprovePendingPermit"] =
                string.Equals(permit.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                && _accessControl.CanApprovePermit(permit, currentUser);
            ViewBag.ReturnUrl =
                !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                    ? returnUrl
                    : null;
            ViewBag.FocusActions = focusActions;

            return View(permit);
        }

        [Authorize(Policy = AppPolicies.ApproveLeaveRequests)]
        public IActionResult LeaveRequest(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            if (!CanOpenLeaveRequest(permit))
            {
                this.ToastError("هذا التصريح غير مؤهل لطلب استئذان حاليًا.");
                return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
            }

            ViewData["InitializeCurrentTime"] = true;

            return View(
                PermitUiModelBuilder.BuildLeaveRequestViewModel(permit, _systemClock.LocalNow)
            );
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ApproveLeaveRequests)]
        [ValidateAntiForgeryToken]
        public IActionResult LeaveRequest(PermitExitRequestViewModel model)
        {
            var permit = _permitService.GetPermitByNumber(model.PermitNumber, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            if (!CanOpenLeaveRequest(permit))
            {
                this.ToastError("هذا التصريح غير مؤهل لطلب استئذان حاليًا.");
                return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
            }

            var viewModel = PermitUiModelBuilder.BuildLeaveRequestViewModel(
                permit,
                _systemClock.LocalNow
            );
            viewModel.RequestMode = model.RequestMode;
            viewModel.ExitMode = model.ExitMode;
            viewModel.LeaveStartAt = model.LeaveStartAt;
            viewModel.LeaveEndAt = model.RequiresReturn ? model.LeaveEndAt : null;
            viewModel.ScheduleStartDate = model.ScheduleStartDate;
            viewModel.ScheduleEndDate = model.ScheduleEndDate;
            viewModel.DailyExitTime = model.DailyExitTime;
            viewModel.DailyReturnTime = model.RequiresReturn ? model.DailyReturnTime : null;
            viewModel.LeaveReason = model.LeaveReason;

            ModelState.Clear();
            TryValidateModel(viewModel);

            if (ModelState.IsValid)
            {
                var administration = _userAdminService.GetAdministrationSettings();
                if (
                    administration == null
                    || (
                        administration.SignatureImageData is not { Length: > 0 }
                        && string.IsNullOrWhiteSpace(administration.SignatureImagePath)
                        && string.IsNullOrWhiteSpace(administration.SignatureText)
                    )
                )
                {
                    this.ToastError(
                        "تعذر اعتماد طلب الاستئذان: توقيع المدير غير محفوظ في لوحة الإدارة. الرجاء رفع توقيع المدير أولًا."
                    );
                    return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
                }

                var updated = _permitService.SubmitLeaveRequest(
                    permit.PermitNumber,
                    viewModel.RequiresReturn,
                    viewModel.LeaveReason,
                    viewModel.LeaveStartAt,
                    viewModel.LeaveEndAt,
                    User.Identity?.Name,
                    viewModel.IsDailyScheduleRequest,
                    viewModel.ScheduleStartDate,
                    viewModel.ScheduleEndDate,
                    viewModel.DailyExitMinutes,
                    viewModel.DailyReturnMinutes
                );

                if (updated)
                {
                    this.ToastSuccess(
                        viewModel.IsDailyScheduleRequest
                            ? viewModel.RequiresReturn
                                ? "تم حفظ الجدولة اليومية بخروج وعودة على نفس التصريح بنجاح."
                                : "تم حفظ الجدولة اليومية بخروج بدون عودة على نفس التصريح بنجاح."
                            : viewModel.RequiresReturn
                                ? "تم اعتماد فترة استئذان خروج وعودة على نفس التصريح بنجاح."
                                : "تم اعتماد فترة استئذان خروج نهائي على نفس التصريح بنجاح."
                    );
                    return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
                }

                ModelState.AddModelError(
                    string.Empty,
                    "تعذر حفظ طلب الاستئذان. راجع حالة التصريح ثم حاول مرة أخرى."
                );
            }

            return View(viewModel);
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.CreatePermits)]
        public IActionResult Create(Permit permit)
        {
            NormalizePermitInput(permit, isCreate: true);
            EnforceDepartmentScope(permit);
            ApplyEmployeeApprovalRoute(permit);

            if (permit.IsVisitorPermit)
            {
                if (!User.HasPermission(AppPermissions.CreateVisitorPermit))
                {
                    return Forbid();
                }

                permit.ExpiresAt = null;
            }
            else if (!User.HasPermission(AppPermissions.CreatePermit))
            {
                return Forbid();
            }

            if (!permit.IsVisitorPermit)
            {
                var permitDate = permit.PermitDate ?? _systemClock.LocalNow;
                if (!permit.ExpiresAt.HasValue || permit.ExpiresAt.Value <= permitDate)
                {
                    permit.ExpiresAt = permitDate.AddMinutes(1);
                }
            }

            ModelState.Clear();
            TryValidateModel(permit);

            if (ModelState.IsValid)
            {
                var conflicts = _permitService.FindPermitConflicts(permit);
                if (conflicts.Count > 0)
                {
                    ViewData["PermitConflicts"] = conflicts;
                    ModelState.AddModelError(
                        string.Empty,
                        "يوجد تصريح مسجل مسبقًا بنفس رقم الهوية أو الاسم أو رقم اللوحة. هل ترغب بالتعديل بدل إنشاء تصريح جديد؟"
                    );
                    PopulateDepartmentOptions(permit.DepartmentName);
                    return View(permit);
                }

                try
                {
                    permit.ApprovalStatus = permit.IsVisitorPermit
                        ? Permit.ApprovalStatusPending
                        : Permit.ApprovalStatusPendingReview;
                    _permitService.AddPermit(permit, User.Identity?.Name);
                    this.ToastSuccess("تم حفظ التصريح بنجاح.");
                    return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
            }

            PopulateDepartmentOptions(permit.DepartmentName);
            return View(permit);
        }

        [Authorize(Policy = AppPolicies.EditPermits)]
        public IActionResult Edit(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            PopulateDepartmentOptions(permit.DepartmentName);
            return View(permit);
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.EditPermits)]
        public IActionResult Edit(Permit permit)
        {
            var existingPermit = _permitService.GetPermitByNumber(
                permit.PermitNumber,
                User.Identity?.Name
            );
            if (existingPermit == null)
            {
                return NotFound();
            }

            permit.PermitDate = existingPermit.PermitDate;
            permit.PermitType = existingPermit.PermitType;
            NormalizePermitInput(permit, isCreate: false);
            EnforceDepartmentScope(permit);
            ApplyEmployeeApprovalRoute(permit);

            if (existingPermit.IsVisitorPermit)
            {
                if (!User.HasPermission(AppPermissions.EditVisitorPermit))
                {
                    return Forbid();
                }
            }
            else if (!User.HasPermission(AppPermissions.EditPermit))
            {
                return Forbid();
            }
            ModelState.Clear();
            TryValidateModel(permit);

            if (ModelState.IsValid)
            {
                var conflicts = _permitService.FindPermitConflicts(permit, permit.PermitNumber);
                if (conflicts.Count > 0)
                {
                    ViewData["PermitConflicts"] = conflicts;
                    ModelState.AddModelError(
                        string.Empty,
                        "يوجد تصريح آخر مطابق بنفس رقم الهوية أو الاسم أو رقم اللوحة. راجع التصريح المتكرر قبل الحفظ."
                    );
                    PopulateDepartmentOptions(permit.DepartmentName);
                    return View(permit);
                }

                _permitService.UpdatePermit(permit, User.Identity?.Name);
                this.ToastSuccess("تم حفظ التعديلات على التصريح بنجاح.");
                return RedirectToAction(nameof(Index));
            }

            PopulateDepartmentOptions(permit.DepartmentName);
            return View(permit);
        }

        private void PopulateDepartmentOptions(string? currentDepartment = null)
        {
            var departments = _userAdminService.GetDepartments().ToList();
            var options = departments
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (
                currentUser != null
                && string.Equals(
                    currentUser.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                var scopedDepartment = (currentUser.Department ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(scopedDepartment))
                {
                    options = new List<string> { scopedDepartment };
                }
            }
            var selectedDepartment = (currentDepartment ?? string.Empty).Trim();
            if (
                !string.IsNullOrWhiteSpace(selectedDepartment)
                && !options.Any(x =>
                    string.Equals(x, selectedDepartment, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                options.Insert(0, selectedDepartment);
            }

            if (options.Count == 0)
            {
                options.Add("الإدارة العامة");
            }

            ViewBag.DepartmentOptions = options;
            ViewBag.DepartmentApprovalRoutesJson = "{}";
            ViewBag.ApprovalRouteHint = "يراجع المدقق الطلب ثم يرفعه لاعتماد مدير الأمن.";
            ViewData["LeaveRequestsEnabled"] = _userAdminService
                .GetAdministrationSettings()
                .LeaveRequestsEnabled;
        }

        private void ApplyEmployeeApprovalRoute(Permit permit)
        {
            if (permit.IsVisitorPermit)
            {
                return;
            }

            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (
                currentUser == null
                || !string.Equals(
                    currentUser.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                permit.ManagerName = string.Empty;
                return;
            }

            var departments = _userAdminService.GetDepartments().ToList();
            var approvalRoutes = PermitDepartmentApprovalRoutesBuilder.BuildDepartmentApprovalRoutes(
                _userAdminService,
                departments
            );
            permit.ManagerName = approvalRoutes.TryGetValue(
                permit.DepartmentName.Trim(),
                out var routeName
            )
                ? routeName
                : string.Empty;
        }

        private bool CanForwardApproval(Permit permit)
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            return CanForwardApproval(permit, currentUser);
        }

        private static bool CanForwardApproval(Permit permit, UserAccount? currentUser)
        {
            if (
                permit.IsVisitorPermit
                || permit.ArchivedAt.HasValue
                || !string.Equals(
                    permit.ApprovalStatus,
                    Permit.ApprovalStatusPendingReview,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            if (currentUser == null || !currentUser.IsActive)
            {
                return false;
            }

            return AppRoles.IsSuperAdmin(currentUser)
                || (
                    string.Equals(
                        currentUser.Role,
                        AppRoles.PermitReviewer,
                        StringComparison.OrdinalIgnoreCase
                    ) && currentUser.CanEditPermit
                );
        }

        private static string NormalizeDepartmentName(string? departmentName)
        {
            return (departmentName ?? string.Empty).Trim();
        }

        private void EnforceDepartmentScope(Permit permit)
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (
                currentUser == null
                || !string.Equals(
                    currentUser.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            var department = (currentUser.Department ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(department) || permit.IsVisitorPermit)
            {
                return;
            }

            permit.DepartmentName = department;
            permit.EmployeeDepartment = department;
        }

        private void NormalizePermitInput(Permit permit, bool isCreate)
        {
            permit.DriverName = (permit.DriverName ?? string.Empty).Trim();
            permit.NationalId = OnlineEditionSettings.HideSensitiveIdentityFields(_configuration)
                ? (
                    string.IsNullOrWhiteSpace(permit.NationalId)
                        ? OnlineEditionSettings.BuildSyntheticNationalId(
                            permit.DriverName,
                            permit.EmployeePhone,
                            permit.PlateNumber,
                            permit.VisitLocation,
                            permit.DepartmentName
                        )
                        : new string(permit.NationalId.Where(char.IsDigit).ToArray())
                )
                : new string((permit.NationalId ?? string.Empty).Where(char.IsDigit).ToArray());
            permit.DepartmentName = (permit.DepartmentName ?? string.Empty).Trim();
            permit.VisitLocation = (permit.VisitLocation ?? string.Empty).Trim();
            permit.ManagerName = (permit.ManagerName ?? string.Empty).Trim();
            permit.EmployeePhone = (permit.EmployeePhone ?? string.Empty).Trim();
            permit.VehicleType = (permit.VehicleType ?? string.Empty).Trim();
            permit.PlateNumber = PermitInputNormalizer.NormalizePlateNumber(permit.PlateNumber);
            permit.PermitType = PermitInputNormalizer.NormalizePermitType(permit.PermitType);
            if (isCreate)
            {
                permit.PermitDate = _systemClock.LocalNow;
            }

            if (permit.IsVisitorPermit)
            {
                permit.DepartmentName = string.Empty;
                permit.ManagerName = string.Empty;
                permit.EmployeeDepartment = permit.VisitLocation;
                permit.Subject = "تصريح زائر بمركبة";
                permit.RequiresReturn = false;
                permit.AccessMode = Permit.AccessModeFullAccess;
            }
            else
            {
                permit.VisitLocation = string.Empty;
                permit.EmployeeDepartment = permit.DepartmentName;
                permit.Subject =
                    permit.PermitType == Permit.PermitTypePermanent
                        ? "تصريح مركبة دائم"
                        : "تصريح مركبة";
            }

            permit.AuthorizingEntity = string.Empty;
            permit.OfficerName = string.Empty;
            permit.EmployeeNumber = string.Empty;
            permit.JobTitle = string.Empty;
            permit.RequiresReturn = permit.PermitType switch
            {
                Permit.PermitTypeExit => permit.RequiresReturn,
                Permit.PermitTypePermanent => permit.RequiresReturn,
                _ when permit.IsVisitorPermit => false,
                _ => true,
            };
            if (
                permit.IsPermanentPermit
                && !_userAdminService.GetAdministrationSettings().LeaveRequestsEnabled
            )
            {
                permit.RequiresReturn = true;
            }
            permit.AccessMode =
                permit.PermitType == Permit.PermitTypePermanent
                    ? (
                        permit.RequiresReturn
                            ? Permit.AccessModeFullAccess
                            : Permit.AccessModeEntryOnly
                    )
                    : Permit.AccessModeFullAccess;
            permit.NormalizeAccessModeState();
        }

        private bool CanOpenLeaveRequest(Permit permit)
        {
            return _userAdminService.GetAdministrationSettings().LeaveRequestsEnabled
                && permit.IsPermanentPermit
                && string.Equals(
                    permit.ApprovalStatus,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase
                )
                && !permit.ArchivedAt.HasValue
                && string.Equals(permit.CurrentState, "Inside", StringComparison.OrdinalIgnoreCase)
                && permit.IsEntryOnlyPermit;
        }

        [Authorize(Policy = AppPolicies.EditPermits)]
        public IActionResult Delete(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (!_accessControl.CanAccessPermit(permit, currentUser))
            {
                return Forbid();
            }

            return View(permit);
        }

        [HttpPost, ActionName("Delete")]
        [Authorize(Policy = AppPolicies.EditPermits)]
        public IActionResult DeleteConfirmed(string id)
        {
            if (_permitService.GetPermitByNumber(id, User.Identity?.Name) == null)
            {
                return NotFound();
            }

            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (permit == null || !_accessControl.CanAccessPermit(permit, currentUser))
            {
                return Forbid();
            }

            _permitService.DeletePermit(id, User.Identity?.Name);
            this.ToastSuccess("تم حذف التصريح بنجاح.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.EditPermits)]
        public IActionResult CancelExpired()
        {
            _permitService.CancelExpiredPermits(User.Identity?.Name);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ViewPermits)]
        [ValidateAntiForgeryToken]
        public IActionResult ForwardToGeneralManager(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            if (!CanForwardApproval(permit))
            {
                return Forbid();
            }

            if (!_permitService.ForwardPermitToGeneralManager(id, User.Identity?.Name))
            {
                this.ToastError(
                    "تعذر رفع الطلب. تأكد من وجود مدير أمن نشط ثم حاول مرة أخرى."
                );
                return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
            }

            this.ToastSuccess("اكتمل التدقيق وتم رفع الطلب لاعتماد مدير الأمن.");
            return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
        }

        [HttpGet]
        [Authorize(Policy = AppPolicies.ApprovePermits)]
        public IActionResult Approve(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (
                !string.Equals(
                    permit.ApprovalStatus,
                    Permit.ApprovalStatusPending,
                    StringComparison.OrdinalIgnoreCase
                )
                || !_accessControl.CanApprovePermit(permit, currentUser)
            )
            {
                return Forbid();
            }

            var administration = _userAdminService.GetAdministrationSettings();
            if (
                administration == null
                || (
                    administration.SignatureImageData is not { Length: > 0 }
                    && string.IsNullOrWhiteSpace(administration.SignatureImagePath)
                    && string.IsNullOrWhiteSpace(administration.SignatureText)
                )
            )
            {
                this.ToastError(
                    "تعذر اعتماد التصريح: توقيع المدير غير محفوظ في لوحة الإدارة. الرجاء رفع صورة توقيع في صفحة بيانات الإدارة أولًا."
                );
                return RedirectToAction(nameof(Details), new { id = id });
            }

            var model = new PermitApproveViewModel
            {
                PermitNumber = permit.PermitNumber,
                DriverName = permit.DriverName,
                PermitTypeDisplay = permit.PermitTypeDisplay,
                ReturnRequirementDisplay = permit.ReturnRequirementDisplay,
            };

            return View(model);
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ApprovePermits)]
        [ValidateAntiForgeryToken]
        public IActionResult Approve(PermitApproveViewModel model, string? returnUrl = null)
        {
            if (model == null)
            {
                return BadRequest();
            }

            var permit = _permitService.GetPermitByNumber(model.PermitNumber, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (
                !string.Equals(
                    permit.ApprovalStatus,
                    Permit.ApprovalStatusPending,
                    StringComparison.OrdinalIgnoreCase
                )
                || !_accessControl.CanApprovePermit(permit, currentUser)
            )
            {
                return Forbid();
            }

            var administration = _userAdminService.GetAdministrationSettings();
            if (
                administration == null
                || (
                    administration.SignatureImageData is not { Length: > 0 }
                    && string.IsNullOrWhiteSpace(administration.SignatureImagePath)
                    && string.IsNullOrWhiteSpace(administration.SignatureText)
                )
            )
            {
                this.ToastError(
                    "تعذر اعتماد التصريح: توقيع المدير غير محفوظ في لوحة الإدارة. الرجاء رفع صورة توقيع في صفحة بيانات الإدارة أولًا."
                );
                return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
            }

            _permitService.UpdatePermitApprovalStatus(
                model.PermitNumber,
                "Approved",
                performedBy: User.Identity?.Name
            );

            this.ToastSuccess("تم اعتماد التصريح بنجاح.");
            return RedirectToLocal(returnUrl);
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ApprovePermits)]
        public IActionResult Reject(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (permit == null)
            {
                return NotFound();
            }

            if (
                !string.Equals(
                    permit.ApprovalStatus,
                    Permit.ApprovalStatusPending,
                    StringComparison.OrdinalIgnoreCase
                )
                || !_accessControl.CanApprovePermit(permit, currentUser)
            )
            {
                return Forbid();
            }

            _permitService.UpdatePermitApprovalStatus(id, "Rejected", User.Identity?.Name);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.StopPermits)]
        public IActionResult Stop(string id, string? returnUrl = null)
        {
            if (_permitService.GetPermitByNumber(id, User.Identity?.Name) == null)
            {
                return NotFound();
            }

            _permitService.StopPermit(id, User.Identity?.Name);
            return RedirectToLocal(returnUrl);
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.StopPermits)]
        public IActionResult Reactivate(string id, string? returnUrl = null)
        {
            if (_permitService.GetPermitByNumber(id, User.Identity?.Name) == null)
            {
                return NotFound();
            }

            var reactivated = _permitService.ReactivatePermit(id, User.Identity?.Name);
            if (reactivated)
            {
                this.ToastSuccess("تمت إعادة تفعيل التصريح بنجاح");
            }
            else
            {
                this.ToastError("تعذر إعادة التفعيل");
            }

            return RedirectToLocal(returnUrl);
        }

        [HttpPost]
        [Authorize(Policy = AppPolicies.ApproveLeaveRequests)]
        [ValidateAntiForgeryToken]
        public IActionResult ClearDailySchedule(string id, string? returnUrl = null)
        {
            if (_permitService.GetPermitByNumber(id, User.Identity?.Name) == null)
            {
                return NotFound();
            }

            var cleared = _permitService.ClearDailyLeaveSchedule(id, User.Identity?.Name);
            if (cleared)
            {
                this.ToastSuccess("تم إلغاء الجدولة اليومية بنجاح.");
            }
            else
            {
                this.ToastError(
                    "تعذر إلغاء الجدولة اليومية. تأكد من أن التصريح داخل المنشأة ولا توجد دورة خروج جارية."
                );
            }

            return RedirectToLocal(returnUrl);
        }

        public IActionResult Barcode(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            if (!CanRenderIssuedPermitArtifacts(permit))
            {
                return Forbid();
            }

            var qrContent = BuildPermitQrContent(permit);
            if (string.IsNullOrWhiteSpace(qrContent))
            {
                return NotFound();
            }

            var bytes = RenderBarcodeImage(qrContent, BarcodeFormat.QR_CODE, 360, 360, 4);
            return File(bytes, "image/png");
        }

        public IActionResult Qr(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            if (!CanRenderIssuedPermitArtifacts(permit))
            {
                return Forbid();
            }

            var qrContent = BuildPermitQrContent(permit);
            if (string.IsNullOrWhiteSpace(qrContent))
            {
                return NotFound();
            }

            var bytes = RenderBarcodeImage(qrContent, BarcodeFormat.QR_CODE, 360, 360, 4);
            return File(bytes, "image/png");
        }

        [AllowAnonymous]
        [HttpGet("/o/{tenant}/permit/{token}")]
        [HttpGet("/Permits/Verify")]
        public IActionResult Verify(string token, string? tenant = null)
        {
            ApplyPrivatePassResponseHeaders();
            if (!TryResolvePublicTenant(tenant))
            {
                return NotFound();
            }

            var isAuthorized = _permitService.TryValidatePermitQrToken(
                token,
                out var permit,
                out var status,
                out var message
            );
            var administration = permit != null
                ? _userAdminService.GetAdministrationSettings()
                : null;
            ViewData["PermitNotice"] =
                administration != null
                    ? PermitUiModelBuilder.BuildPermitUsageNotice(administration)
                    : string.Empty;

            return View(
                PermitUiModelBuilder.BuildPermitVerificationModel(
                    permit,
                    isAuthorized,
                    status,
                    message,
                    administration
                )
            );
        }

        [AllowAnonymous]
        [HttpGet("/o/{tenant}/pass/{token}")]
        public IActionResult Pass(string token, string tenant)
        {
            if (!TryResolvePublicTenant(tenant))
            {
                return NotFound();
            }

            ApplyPrivatePassResponseHeaders();
            var isAuthorized = _permitService.TryValidatePermitQrToken(
                token,
                out var permit,
                out var status,
                out var message
            );
            var administration = permit != null
                ? _userAdminService.GetAdministrationSettings()
                : null;
            ViewData["Robots"] = "noindex, nofollow, noarchive";
            ViewData["PermitNotice"] = administration != null
                ? PermitUiModelBuilder.BuildPermitUsageNotice(administration)
                : string.Empty;

            var qrImageUrl = isAuthorized
                ? Url.Action(nameof(PassQr), "Permits", new { tenant, token }) ?? string.Empty
                : string.Empty;

            return View(
                PermitUiModelBuilder.BuildPermitDigitalPassModel(
                    permit,
                    isAuthorized,
                    status,
                    message,
                    administration,
                    qrImageUrl
                )
            );
        }

        [AllowAnonymous]
        [HttpGet("/o/{tenant}/pass/{token}/qr")]
        public IActionResult PassQr(string token, string tenant)
        {
            if (!TryResolvePublicTenant(tenant))
            {
                return NotFound();
            }

            ApplyPrivatePassResponseHeaders();
            var isAuthorized = _permitService.TryValidatePermitQrToken(
                token,
                out var permit,
                out _,
                out _
            );
            if (!isAuthorized || permit == null)
            {
                return NotFound();
            }

            var verificationUrl = BuildPermitVerificationUrl(permit, token);
            if (string.IsNullOrWhiteSpace(verificationUrl))
            {
                return NotFound();
            }

            return Content(
                RenderBarcodeSvg(verificationUrl, BarcodeFormat.QR_CODE, 420, 4),
                "image/svg+xml"
            );
        }

        public IActionResult VerifyByNumber(string id)
        {
            var permit = _permitService.GetPermitByNumber(id);
            if (permit == null)
            {
                return View(
                    "Verify",
                    PermitUiModelBuilder.BuildPermitVerificationModel(
                        permit: null,
                        isAuthorized: false,
                        status: "unauthorized",
                        message: "هذا التصريح غير موجود أو غير مصرح به."
                    )
                );
            }

            var status = GetPermitPublicStatus(permit);
            var isAuthorized = string.Equals(
                status,
                "authorized",
                StringComparison.OrdinalIgnoreCase
            );
            var message =
                isAuthorized ? "التصريح فعال ومصرح به."
                : string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase)
                    ? "هذا التصريح منتهي أو غير فعال."
                : "هذا التصريح غير مصرح به حاليًا.";
            var administration = _userAdminService.GetAdministrationSettings();
            ViewData["PermitNotice"] = PermitUiModelBuilder.BuildPermitUsageNotice(administration);

            return View(
                "Verify",
                PermitUiModelBuilder.BuildPermitVerificationModel(
                    permit,
                    isAuthorized,
                    status,
                    message,
                    administration
                )
            );
        }

        public IActionResult Print(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            if (!CanRenderIssuedPermitArtifacts(permit))
            {
                return Forbid();
            }

            var administration = _userAdminService.GetAdministrationSettings();
            var logoBytes = ResolveLogoBytes(administration.LogoPath);
            var signatureBytes = administration.SignatureImageData is { Length: > 0 }
                ? administration.SignatureImageData
                : ResolveLogoBytes(administration.SignatureImagePath);
            var approvalAuthorityDisplay = string.IsNullOrWhiteSpace(administration.DepartmentName)
                ? string.IsNullOrWhiteSpace(administration.OrganizationName)
                    ? "غير محدد"
                    : administration.OrganizationName
                : administration.DepartmentName;
            var managerNameDisplay = string.IsNullOrWhiteSpace(administration.ManagerName)
                ? "غير محدد"
                : administration.ManagerName;
            var signatureTextDisplay = string.IsNullOrWhiteSpace(administration.SignatureText)
                ? "غير محدد"
                : administration.SignatureText;
            var permitNotice = PermitUiModelBuilder.BuildPermitUsageNotice(administration);
            var qrContent = BuildPermitQrContent(permit);
            if (string.IsNullOrWhiteSpace(qrContent))
            {
                return NotFound();
            }
            var gateQrBytes = RenderBarcodeImage(
                qrContent,
                BarcodeFormat.QR_CODE,
                128,
                128,
                4
            );

            var pdfBytes = Document
                .Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(18);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(TextStyle.Default.FontFamily("Tajawal").FontSize(10));

                        page.Content()
                            .ContentFromRightToLeft()
                            .Column(column =>
                            {
                                column.Spacing(10);

                                column
                                    .Item()
                                    .Row(row =>
                                    {
                                        row.Spacing(12);
                                        row.ConstantItem(96)
                                            .Column(code =>
                                            {
                                                code.Spacing(5);
                                                code.Item()
                                                    .AlignCenter()
                                                    .Text("QR البوابة")
                                                    .SemiBold()
                                                    .FontSize(9);
                                                code.Item()
                                                    .Border(1)
                                                    .BorderColor(Colors.Grey.Lighten1)
                                                    .Padding(4)
                                                    .AlignCenter()
                                                    .Image(gateQrBytes)
                                                    .FitArea();
                                            });

                                        row.RelativeItem()
                                            .Column(info =>
                                            {
                                                info.Spacing(4);
                                                info.Item()
                                                    .AlignRight()
                                                    .Text(administration.OrganizationName)
                                                    .Bold()
                                                    .FontSize(18)
                                                    .FontColor(Colors.Blue.Darken2);
                                                info.Item()
                                                    .AlignRight()
                                                    .Text(administration.DepartmentName)
                                                    .SemiBold()
                                                    .FontSize(12);
                                                info.Item()
                                                    .AlignRight()
                                                    .Text($"العنوان: {administration.Address}");
                                                info.Item()
                                                    .AlignRight()
                                                    .Text($"الهاتف: {administration.Phone}");
                                            });

                                        row.ConstantItem(110)
                                            .Height(90)
                                            .Border(1)
                                            .BorderColor(Colors.Blue.Medium)
                                            .Background(Colors.Blue.Lighten5)
                                            .AlignMiddle()
                                            .AlignCenter()
                                            .Element(cell =>
                                            {
                                                if (logoBytes != null)
                                                    cell.Padding(8).Image(logoBytes).FitArea();
                                                else
                                                    cell.Text("VP")
                                                        .Bold()
                                                        .FontSize(28)
                                                        .FontColor(Colors.Blue.Darken2);
                                            });
                                    });

                                column
                                    .Item()
                                    .PaddingTop(4)
                                    .LineHorizontal(1)
                                    .LineColor(Colors.Grey.Lighten1);

                                column
                                    .Item()
                                    .Text("تصريح مرور رسمي")
                                    .Bold()
                                    .FontSize(16)
                                    .AlignCenter();

                                column
                                    .Item()
                                    .Table(table =>
                                    {
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.RelativeColumn(1.25f);
                                            columns.RelativeColumn(2.5f);
                                        });

                                        AddDetailRow(table, "رقم التصريح", permit.PermitNumber);
                                        AddDetailRow(
                                            table,
                                            permit.IsVisitorPermit ? "اسم الزائر" : "اسم المصرح له",
                                            permit.DriverName
                                        );
                                        AddDetailRow(table, "رقم الهوية", permit.NationalId);
                                        AddDetailRow(
                                            table,
                                            permit.RequiresVisitLocation
                                                ? "مكان الزيارة"
                                                : "موقع العمل",
                                            permit.LocationDisplay
                                        );
                                        AddDetailRow(
                                            table,
                                            permit.IsVisitorPermit
                                                ? "مسؤول الزيارة"
                                                : "مدير الموظف",
                                            permit.ManagerName
                                        );
                                        AddDetailRow(table, "رقم الجوال", permit.EmployeePhone);
                                        AddDetailRow(table, "نوع المركبة", permit.VehicleType);
                                        AddDetailRow(
                                            table,
                                            "رقم اللوحة",
                                            permit.PlateNumberDisplay
                                        );
                                        AddDetailRow(table, "الحالة", permit.ApprovalStatusDisplay);
                                        AddDetailRow(
                                            table,
                                            "تاريخ التصريح",
                                            permit.PermitDate.HasValue
                                                ? HijriDateFormatter.Format(permit.PermitDate)
                                                : "غير محدد"
                                        );
                                        AddDetailRow(
                                            table,
                                            "تاريخ الانتهاء",
                                            permit.ExpiresAt.HasValue
                                                ? HijriDateFormatter.Format(permit.ExpiresAt)
                                                : "غير محدد"
                                        );
                                    });

                                column
                                    .Item()
                                    .Border(1)
                                    .BorderColor(Colors.Grey.Lighten1)
                                    .Padding(8)
                                    .Column(block =>
                                    {
                                        block.Spacing(3);
                                        block.Item().Text("اعتماد الإدارة").Bold();
                                        block.Item().Text($"الإدارة: {approvalAuthorityDisplay}");
                                        block.Item().Text($"المدير: {managerNameDisplay}");
                                        block
                                            .Item()
                                            .Text($"التوقيع الإلكتروني: {signatureTextDisplay}");
                                        if (signatureBytes != null)
                                        {
                                            block
                                                .Item()
                                                .PaddingTop(4)
                                                .Height(55)
                                                .Image(signatureBytes)
                                                .FitHeight();
                                        }
                                        else
                                        {
                                            block
                                                .Item()
                                                .Text("لا توجد صورة توقيع مرفقة.")
                                                .FontSize(9)
                                                .FontColor(Colors.Grey.Darken1);
                                        }
                                        block
                                            .Item()
                                            .Text(
                                                "هذا المستند صادر إلكترونيا ومعتمد للاستخدام الرسمي"
                                            )
                                            .FontSize(9)
                                            .FontColor(Colors.Grey.Darken1);
                                    });

                                column
                                    .Item()
                                    .Border(1)
                                    .BorderColor(Colors.Amber.Medium)
                                    .Background(Colors.Amber.Lighten5)
                                    .Padding(8)
                                    .Column(note =>
                                    {
                                        note.Spacing(3);
                                        note.Item().Text("ملاحظة").Bold().FontSize(10);
                                        note.Item()
                                            .Text(permitNotice)
                                            .SemiBold()
                                            .FontSize(10)
                                            .AlignRight();
                                    });
                            });
                    });
                })
                .GeneratePdf();

            return File(pdfBytes, "application/pdf", $"Permit_{permit.PermitNumber}.pdf");
        }

        public IActionResult PrintLabel(
            string id,
            string? preset = null,
            float? widthMm = null,
            float? heightMm = null
        )
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            if (!CanRenderIssuedPermitArtifacts(permit))
            {
                return Forbid();
            }

            var administration = _userAdminService.GetAdministrationSettings();
            var normalizedPreset = NormalizeLabelPreset(preset);
            var defaultLabelWidthMm =
                normalizedPreset == ZebraLabelPreset ? ZebraLabelWidthMm : 100f;
            var defaultLabelHeightMm =
                normalizedPreset == ZebraLabelPreset ? ZebraLabelHeightMm : 50f;
            var labelWidthMm = NormalizeLabelDimension(widthMm, defaultLabelWidthMm, 55f, 120f);
            var labelHeightMm = NormalizeLabelDimension(heightMm, defaultLabelHeightMm, 30f, 80f);
            var compactLabel = labelWidthMm < 85f || labelHeightMm < 42f;
            var qrBoxMm = Math.Clamp(
                Math.Min(labelHeightMm - (compactLabel ? 10f : 14f), labelWidthMm * 0.28f),
                18f,
                compactLabel ? 22f : 28f
            );
            var labelTitle =
                normalizedPreset == ZebraLabelPreset ? "ملصق Zebra للمركبة" : "ملصق تصريح مركبة";
            var labelFooter =
                normalizedPreset == ZebraLabelPreset
                    ? "جاهز للطباعة على Zebra بمقاس 100×50 مم."
                    : "يوضع الملصق على المركبة للتحقق السريع عند البوابة.";
            var qrValue = BuildPermitQrContent(permit);
            if (string.IsNullOrWhiteSpace(qrValue))
            {
                return NotFound();
            }
            var gateQrBytes = RenderBarcodeImage(
                qrValue,
                BarcodeFormat.QR_CODE,
                ConvertMillimetresToPixels(qrBoxMm),
                ConvertMillimetresToPixels(qrBoxMm),
                4
            );
            var organizationName = TruncateLabelValue(
                administration.OrganizationName,
                compactLabel ? 24 : 38
            );
            var permitHolder = TruncateLabelValue(permit.DriverName, compactLabel ? 18 : 26);
            var locationDisplay = TruncateLabelValue(
                permit.LocationDisplay,
                compactLabel ? 18 : 24
            );
            var plateNumberDisplay = TruncateLabelValue(
                permit.PlateNumberDisplay,
                compactLabel ? 18 : 22
            );
            var permitHolderLabel = permit.IsVisitorPermit ? "الزائر" : "المصرح له";
            var locationLabel = permit.RequiresVisitLocation ? "مكان الزيارة" : "موقع العمل";
            var expiryText = BuildPermitLabelExpiryText(permit);

            var pdfBytes = Document
                .Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(labelWidthMm, labelHeightMm, Unit.Millimetre);
                        page.Margin(compactLabel ? 2f : 3f, Unit.Millimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(
                            TextStyle.Default.FontFamily("Tajawal").FontSize(compactLabel ? 7f : 8.5f)
                        );

                        page.Content()
                            .ContentFromRightToLeft()
                            .Border(1)
                            .BorderColor(Colors.Grey.Lighten2)
                            .Padding(compactLabel ? 4 : 6)
                            .Column(column =>
                            {
                                column.Spacing(compactLabel ? 2 : 4);

                                if (compactLabel)
                                {
                                    column
                                        .Item()
                                        .AlignCenter()
                                        .Text(organizationName)
                                        .SemiBold()
                                        .FontSize(6f);
                                    column
                                        .Item()
                                        .AlignCenter()
                                        .Text("ملصق مركبة")
                                        .Bold()
                                        .FontSize(8f);
                                    if (normalizedPreset == ZebraLabelPreset)
                                    {
                                        column
                                            .Item()
                                            .AlignCenter()
                                            .Text("Zebra 100×50 مم")
                                            .FontSize(5.8f)
                                            .FontColor(Colors.Grey.Darken1);
                                    }
                                    column
                                        .Item()
                                        .AlignCenter()
                                        .Text(plateNumberDisplay)
                                        .Bold()
                                        .FontSize(14f);
                                    column
                                        .Item()
                                        .Row(row =>
                                        {
                                            row.Spacing(4);

                                            row.RelativeItem()
                                                .Column(info =>
                                                {
                                                    info.Spacing(1);
                                                    info.Item()
                                                        .Text($"التصريح: {permit.PermitNumber}")
                                                        .FontSize(6.1f);
                                                    info.Item()
                                                        .Text($"الصلاحية: {expiryText}")
                                                        .FontSize(6.1f);
                                                    info.Item().Text(permitHolder).FontSize(6f);
                                                });

                                            row.ConstantItem(qrBoxMm, Unit.Millimetre)
                                                .Border(1)
                                                .BorderColor(Colors.Grey.Lighten1)
                                                .Padding(2)
                                                .AlignCenter()
                                                .Image(gateQrBytes)
                                                .FitArea();
                                        });

                                    if (labelHeightMm >= 34f)
                                    {
                                        column
                                            .Item()
                                            .AlignCenter()
                                            .Text("صالح للمركبة المعتمدة فقط")
                                            .FontSize(5.4f)
                                            .FontColor(Colors.Grey.Darken1);
                                    }
                                }
                                else
                                {
                                    column
                                        .Item()
                                        .Row(row =>
                                        {
                                            row.Spacing(6);

                                            row.RelativeItem()
                                                .Column(info =>
                                                {
                                                    info.Spacing(1);
                                                    info.Item()
                                                        .Text(organizationName)
                                                        .SemiBold()
                                                        .FontSize(8.5f)
                                                        .FontColor(Colors.Blue.Darken2);
                                                    info.Item()
                                                        .Text(labelTitle)
                                                        .Bold()
                                                        .FontSize(9.5f);
                                                    info.Item()
                                                        .Text(plateNumberDisplay)
                                                        .Bold()
                                                        .FontSize(18f);
                                                    info.Item()
                                                        .Text(
                                                            $"رقم التصريح: {permit.PermitNumber}"
                                                        );
                                                    info.Item()
                                                        .Text(
                                                            $"{permitHolderLabel}: {permitHolder}"
                                                        );
                                                    info.Item()
                                                        .Text(
                                                            $"{locationLabel}: {locationDisplay}"
                                                        );
                                                    info.Item().Text($"الصلاحية: {expiryText}");
                                                });

                                            row.ConstantItem(qrBoxMm, Unit.Millimetre)
                                                .Column(code =>
                                                {
                                                    code.Spacing(2);
                                                    code.Item()
                                                        .Border(1)
                                                        .BorderColor(Colors.Grey.Lighten1)
                                                        .Padding(3)
                                                        .AlignCenter()
                                                        .Image(gateQrBytes)
                                                        .FitArea();
                                                    code.Item()
                                                        .AlignCenter()
                                                        .Text("QR البوابة")
                                                        .FontSize(6.5f);
                                                });
                                        });

                                    column
                                        .Item()
                                        .AlignCenter()
                                        .Text(labelFooter)
                                        .FontSize(6.4f)
                                        .FontColor(Colors.Grey.Darken1);
                                }
                            });
                    });
                })
                .GeneratePdf();

            return File(
                pdfBytes,
                "application/pdf",
                $"PermitLabel_{BuildLabelFileNamePrefix(normalizedPreset)}{permit.PermitNumber}_{FormatLabelDimension(labelWidthMm)}x{FormatLabelDimension(labelHeightMm)}.pdf"
            );
        }

        public IActionResult PrintZebraLabel(string id)
        {
            var permit = _permitService.GetPermitByNumber(id, User.Identity?.Name);
            if (permit == null)
            {
                return NotFound();
            }

            if (!CanRenderIssuedPermitArtifacts(permit))
            {
                return Forbid();
            }

            var labelContent = BuildZebraLabelContent(permit);
            if (string.IsNullOrWhiteSpace(labelContent.BarcodeValue))
            {
                return NotFound();
            }

            var qrBoxMm = 38f;
            var gateQrBytes = RenderBarcodeImage(
                labelContent.BarcodeValue,
                BarcodeFormat.QR_CODE,
                ConvertMillimetresToPixels(qrBoxMm),
                ConvertMillimetresToPixels(qrBoxMm),
                4
            );

            var pdfBytes = Document
                .Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(ZebraLabelWidthMm, ZebraLabelHeightMm, Unit.Millimetre);
                        page.Margin(3f, Unit.Millimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(TextStyle.Default.FontFamily("Tajawal"));

                        page.Content()
                            .ContentFromRightToLeft()
                            .Border(1)
                            .BorderColor(Colors.Black)
                            .Padding(4)
                            .Row(row =>
                            {
                                row.Spacing(6);

                                row.RelativeItem()
                                    .AlignMiddle()
                                    .AlignCenter()
                                    .Text(labelContent.PlateNumberDisplay)
                                    .Bold()
                                    .FontSize(28)
                                    .FontColor(Colors.Black);

                                row.ConstantItem(qrBoxMm, Unit.Millimetre)
                                    .AlignMiddle()
                                    .Border(1)
                                    .BorderColor(Colors.Grey.Lighten1)
                                    .Padding(2)
                                    .Image(gateQrBytes)
                                    .FitArea();
                            });
                    });
                })
                .GeneratePdf();

            return File(pdfBytes, "application/pdf", $"PermitZebraLabel_{permit.PermitNumber}.pdf");
        }

        private static void AddDetailRow(TableDescriptor table, string label, string value)
        {
            table
                .Cell()
                .Border(1)
                .BorderColor(Colors.Grey.Lighten1)
                .Background(Colors.Grey.Lighten4)
                .PaddingVertical(5)
                .PaddingHorizontal(8)
                .AlignMiddle()
                .Text(label)
                .SemiBold();
            table
                .Cell()
                .Border(1)
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingVertical(5)
                .PaddingHorizontal(8)
                .AlignMiddle()
                .Text(string.IsNullOrWhiteSpace(value) ? "-" : value);
        }

        private static string BuildPermitLabelExpiryText(Permit permit)
        {
            if (permit.ExpiresAt.HasValue)
            {
                return HijriDateFormatter.Format(permit.ExpiresAt);
            }

            return permit.IsVisitorPermit ? "حتى نهاية الزيارة" : "غير محدد";
        }

        private static string? NormalizeLabelPreset(string? preset)
        {
            if (string.Equals(preset?.Trim(), ZebraLabelPreset, StringComparison.OrdinalIgnoreCase))
            {
                return ZebraLabelPreset;
            }

            return null;
        }

        private static string BuildLabelFileNamePrefix(string? preset)
        {
            return preset == ZebraLabelPreset ? "Zebra_" : string.Empty;
        }

        private ZebraLabelContent BuildZebraLabelContent(Permit permit)
        {
            return new ZebraLabelContent(
                BuildPermitQrContent(permit),
                string.IsNullOrWhiteSpace(permit.PlateNumberDisplay)
                    ? permit.PlateNumber.Trim()
                    : permit.PlateNumberDisplay.Trim()
            );
        }

        private static float NormalizeLabelDimension(
            float? requestedValue,
            float fallbackValue,
            float minValue,
            float maxValue
        )
        {
            if (
                !requestedValue.HasValue
                || float.IsNaN(requestedValue.Value)
                || float.IsInfinity(requestedValue.Value)
            )
            {
                return fallbackValue;
            }

            return Math.Clamp(requestedValue.Value, minValue, maxValue);
        }

        private static int ConvertMillimetresToPixels(float millimetres)
        {
            return Math.Max(128, (int)Math.Round(Math.Max(millimetres, 1f) * 12f));
        }

        private static string TruncateLabelValue(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "غير محدد";
            }

            var normalized = value.Trim();
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized[..Math.Max(0, maxLength - 3)] + "...";
        }

        private static string FormatLabelDimension(float value)
        {
            return Math.Round(value).ToString("0");
        }

        private static bool CanRenderIssuedPermitArtifacts(Permit permit)
        {
            return string.Equals(
                permit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        private string BuildPermitVerificationUrl(Permit permit, string token)
        {
            var tenantReference = _userAdminService
                .GetTenants(includeInactive: true)
                .FirstOrDefault(item =>
                    string.Equals(
                        item.TenantId,
                        permit.TenantId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                ?.Slug;

            tenantReference = string.IsNullOrWhiteSpace(tenantReference)
                ? permit.TenantId
                : tenantReference;

            return BuildPermitPublicUrl(
                nameof(Verify),
                new { tenant = tenantReference, token }
            );
        }

        private string BuildPermitDigitalPassUrl(Permit permit, string token)
        {
            var tenantReference = _userAdminService
                .GetTenants(includeInactive: true)
                .FirstOrDefault(item =>
                    string.Equals(
                        item.TenantId,
                        permit.TenantId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                ?.Slug;

            tenantReference = string.IsNullOrWhiteSpace(tenantReference)
                ? permit.TenantId
                : tenantReference;

            return BuildPermitPublicUrl(
                nameof(Pass),
                new { tenant = tenantReference, token }
            );
        }

        private string BuildPermitQrContent(Permit permit)
        {
            if (PermitQrTokenGenerator.IsCurrentVersion(permit.QrToken))
            {
                var verificationUrl = BuildPermitDigitalPassUrl(permit, permit.QrToken);
                if (!string.IsNullOrWhiteSpace(verificationUrl))
                {
                    return verificationUrl;
                }

                return permit.QrToken;
            }

            var persistedToken = _permitService.EnsurePermitQrToken(
                permit.PermitNumber,
                User.Identity?.Name
            );
            if (!string.IsNullOrWhiteSpace(persistedToken))
            {
                var verificationUrl = BuildPermitDigitalPassUrl(permit, persistedToken);
                if (!string.IsNullOrWhiteSpace(verificationUrl))
                {
                    return verificationUrl;
                }

                return persistedToken;
            }

            return string.Empty;
        }

        private bool TryResolvePublicTenant(string? tenantReference)
        {
            if (string.IsNullOrWhiteSpace(tenantReference))
            {
                return true;
            }

            var tenant = _userAdminService
                .GetTenants(includeInactive: true)
                .FirstOrDefault(item =>
                    string.Equals(
                        item.TenantId,
                        tenantReference,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        item.Slug,
                        tenantReference,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            if (tenant == null || !tenant.IsActive)
            {
                return false;
            }

            HttpContext.Items[HttpTenantContext.ResolvedTenantItemKey] = tenant.TenantId;
            return true;
        }

        private static string BuildPermitUsageNotice(AdministrationSettings administration)
        {
            var departmentName = string.IsNullOrWhiteSpace(administration.DepartmentName)
                ? administration.OrganizationName
                : administration.DepartmentName;

            return $"هذا التصريح مخصص لدخول المبنى التابع لـ {departmentName}، ولا يُستخدم لأي جهة أخرى. ويتحمل من يُبرز هذا التصريح أو يستخدمه لغير الجهة الرسمية المخوَّل لها كامل المسؤولية.";
        }

        private string BuildPermitVerificationByNumberUrl(string permitNumber)
        {
            return BuildPermitPublicUrl(nameof(VerifyByNumber), new { id = permitNumber });
        }

        private string BuildPermitPublicUrl(string actionName, object routeValues)
        {
            var path = Url.Action(actionName, "Permits", routeValues) ?? $"/Permits/{actionName}";
            var configuredBaseUrl = _userAdminService.GetAdministrationSettings().DisplayBaseUrl;
            if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
            {
                return $"{configuredBaseUrl.TrimEnd('/')}{path}";
            }

            return Url.Action(actionName, "Permits", routeValues, Request.Scheme) ?? path;
        }

        private void ApplyPrivatePassResponseHeaders()
        {
            Response.Headers.CacheControl = "no-store, private";
            Response.Headers.Pragma = "no-cache";
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            Response.Headers["Referrer-Policy"] = "no-referrer";
        }

        private string GetPermitPublicStatus(Permit permit)
        {
            if (
                string.Equals(permit.ApprovalStatus, "Rejected", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    permit.ApprovalStatus,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    permit.ApprovalStatus,
                    "Stopped",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "unauthorized";
            }

            if (
                permit.ArchivedAt.HasValue
                || string.Equals(
                    permit.ApprovalStatus,
                    "Expired",
                    StringComparison.OrdinalIgnoreCase
                )
                || (permit.ExpiresAt.HasValue && permit.ExpiresAt.Value <= _systemClock.LocalNow)
            )
            {
                return "expired";
            }

            return "authorized";
        }

        private byte[]? ResolveLogoBytes(string? logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath))
            {
                return null;
            }

            var normalizedPath = logoPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart('~', '/', '\\');

            var candidatePaths = Path.IsPathRooted(logoPath)
                ? new[] { logoPath }
                : new[]
                {
                    AppStoragePaths.ResolveUploadPhysicalPath(logoPath),
                    Path.Combine(_environment.WebRootPath, normalizedPath),
                    Path.Combine(_environment.ContentRootPath, normalizedPath),
                    Path.Combine(_environment.ContentRootPath, logoPath),
                };

            var resolvedPath = candidatePaths.FirstOrDefault(System.IO.File.Exists);

            return !string.IsNullOrWhiteSpace(resolvedPath)
                ? System.IO.File.ReadAllBytes(resolvedPath)
                : null;
        }

        private static string RenderBarcodeSvg(
            string content,
            BarcodeFormat format,
            int size,
            int margin
        )
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = format,
                Options = new EncodingOptions
                {
                    Width = size,
                    Height = size,
                    Margin = margin,
                    PureBarcode = true,
                },
            };
            var pixelData = writer.Write(content);
            var path = new System.Text.StringBuilder(pixelData.Width * 8);

            for (var y = 0; y < pixelData.Height; y++)
            {
                var x = 0;
                while (x < pixelData.Width)
                {
                    var offset = ((y * pixelData.Width) + x) * 4;
                    if (pixelData.Pixels[offset] >= 128)
                    {
                        x++;
                        continue;
                    }

                    var start = x;
                    while (x < pixelData.Width)
                    {
                        offset = ((y * pixelData.Width) + x) * 4;
                        if (pixelData.Pixels[offset] >= 128)
                        {
                            break;
                        }

                        x++;
                    }

                    path.Append('M')
                        .Append(start)
                        .Append(' ')
                        .Append(y)
                        .Append('h')
                        .Append(x - start)
                        .Append("v1h-")
                        .Append(x - start)
                        .Append('z');
                }
            }

            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {pixelData.Width} {pixelData.Height}\" shape-rendering=\"crispEdges\"><rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path d=\"{path}\" fill=\"#050914\"/></svg>";
        }

        private static byte[] RenderBarcodeImage(
            string content,
            BarcodeFormat format,
            int width,
            int height,
            int margin = 0
        )
        {
            return BarcodePngRenderer.Render(content, format, width, height, margin);
        }
    }
}
