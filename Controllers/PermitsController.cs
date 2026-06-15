using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;
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

        public PermitsController(
            IPermitService permitService,
            IUserAdminService userAdminService,
            IAccessControlService accessControl,
            IWebHostEnvironment environment,
            ISystemClock systemClock
        )
        {
            _permitService = permitService;
            _userAdminService = userAdminService;
            _accessControl = accessControl;
            _environment = environment;
            _systemClock = systemClock;
        }

        [HttpGet]
        public IActionResult Index(string searchTerm = "", int page = 1, int pageSize = 5)
        {
            var normalizedPageSize = PermitInputNormalizer.NormalizePageSize(pageSize);
            var hasSearchTerm = !string.IsNullOrWhiteSpace(searchTerm);
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
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
            var pendingApprovalCount = permits.Count(p =>
                string.Equals(p.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                && _accessControl.CanApprovePermit(p, currentUser)
            );

            var model = new PermitIndexViewModel
            {
                SearchTerm = searchTerm,
                ApprovablePermitNumbers = approvablePermitNumbers,
                PendingApprovalCount = pendingApprovalCount,
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
                        string.IsNullOrWhiteSpace(administration.SignatureImagePath)
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

                permit.ApprovalStatus = "Pending";
                _permitService.AddPermit(permit, User.Identity?.Name);
                this.ToastSuccess("تم حفظ التصريح بنجاح.");
                return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
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
            var approvalRoutes =
                PermitDepartmentApprovalRoutesBuilder.BuildDepartmentApprovalRoutes(
                    _userAdminService,
                    departments
                );
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
            ViewBag.DepartmentApprovalRoutesJson = JsonSerializer.Serialize(approvalRoutes);
            ViewBag.ApprovalRouteHint =
                "سيُوجَّه الطلب إلى مدير القسم أولًا، ويمكنه رفعه إلى المدير العام إذا لم تتوفر لديه صلاحية الاعتماد.";
        }

        private void ApplyEmployeeApprovalRoute(Permit permit)
        {
            if (permit.IsVisitorPermit)
            {
                return;
            }

            var departments = _userAdminService.GetDepartments().ToList();
            var selectedDepartment = departments.FirstOrDefault(department =>
                string.Equals(
                    department.Name,
                    permit.DepartmentName,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            var approvalRoutes =
                PermitDepartmentApprovalRoutesBuilder.BuildDepartmentApprovalRoutes(
                    _userAdminService,
                    departments
                );
            var routeName = string.Empty;
            if (
                selectedDepartment != null
                && approvalRoutes.TryGetValue(selectedDepartment.Name.Trim(), out var resolvedRoute)
            )
            {
                routeName = resolvedRoute;
            }

            if (string.IsNullOrWhiteSpace(routeName))
            {
                routeName = permit.ManagerName?.Trim();
            }

            permit.ManagerName = routeName ?? string.Empty;
        }

        private bool CanForwardApproval(Permit permit)
        {
            if (permit.IsVisitorPermit || permit.ArchivedAt.HasValue)
            {
                return false;
            }

            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (currentUser == null || !currentUser.IsActive || currentUser.CanApprovePermit)
            {
                return false;
            }

            return _accessControl.IsCurrentDepartmentManager(currentUser, permit.EmployeeDepartment)
                || _accessControl.IsCurrentDepartmentManager(currentUser, permit.DepartmentName);
        }

        private static string NormalizeDepartmentName(string? departmentName)
        {
            return (departmentName ?? string.Empty).Trim();
        }

        private void EnforceDepartmentScope(Permit permit)
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (currentUser == null)
            {
                return;
            }

            if (
                !string.Equals(
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

        private static bool CanOpenLeaveRequest(Permit permit)
        {
            return permit.IsPermanentPermit
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
                    "تعذر رفع الطلب للمدير العام. تأكد من وجود مدير عام صالح ثم حاول مرة أخرى."
                );
                return RedirectToAction(nameof(Details), new { id = permit.PermitNumber });
            }

            this.ToastSuccess("تم رفع الطلب إلى المدير العام بنجاح.");
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
            if (!_accessControl.CanApprovePermit(permit, currentUser))
            {
                return Forbid();
            }

            var administration = _userAdminService.GetAdministrationSettings();
            if (
                administration == null
                || (
                    string.IsNullOrWhiteSpace(administration.SignatureImagePath)
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
            if (!_accessControl.CanApprovePermit(permit, currentUser))
            {
                return Forbid();
            }

            var administration = _userAdminService.GetAdministrationSettings();
            if (
                administration == null
                || (
                    string.IsNullOrWhiteSpace(administration.SignatureImagePath)
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

            if (!_accessControl.CanApprovePermit(permit, currentUser))
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

        [SupportedOSPlatform("windows")]
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

            var bytes = RenderBarcodeImage(
                permit.PublicPermitCode,
                BarcodeFormat.QR_CODE,
                300,
                300,
                2
            );
            return File(bytes, "image/png");
        }

        [SupportedOSPlatform("windows")]
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

            var bytes = RenderBarcodeImage(qrContent, BarcodeFormat.QR_CODE, 260, 260, 1);
            return File(bytes, "image/png");
        }

        [AllowAnonymous]
        public IActionResult Verify(string token)
        {
            var isAuthorized = _permitService.TryValidatePermitQrToken(
                token,
                out var permit,
                out var status,
                out var message
            );
            ViewData["PermitNotice"] =
                permit != null
                    ? PermitUiModelBuilder.BuildPermitUsageNotice(
                        _userAdminService.GetAdministrationSettings()
                    )
                    : string.Empty;

            return View(
                PermitUiModelBuilder.BuildPermitVerificationModel(
                    permit,
                    isAuthorized,
                    status,
                    message
                )
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
            ViewData["PermitNotice"] = PermitUiModelBuilder.BuildPermitUsageNotice(
                _userAdminService.GetAdministrationSettings()
            );

            return View(
                "Verify",
                PermitUiModelBuilder.BuildPermitVerificationModel(
                    permit,
                    isAuthorized,
                    status,
                    message
                )
            );
        }

        [SupportedOSPlatform("windows")]
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
            var signatureBytes = ResolveLogoBytes(administration.SignatureImagePath);
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
            var gateQrBytes = RenderBarcodeImage(
                permit.PublicPermitCode,
                BarcodeFormat.QR_CODE,
                128,
                128,
                2
            );

            var pdfBytes = Document
                .Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(18);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(TextStyle.Default.FontFamily("Arial").FontSize(10));

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

        [SupportedOSPlatform("windows")]
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
            var qrValue = permit.PublicPermitCode;
            if (string.IsNullOrWhiteSpace(qrValue))
            {
                return NotFound();
            }
            var gateQrBytes = RenderBarcodeImage(
                qrValue,
                BarcodeFormat.QR_CODE,
                ConvertMillimetresToPixels(qrBoxMm),
                ConvertMillimetresToPixels(qrBoxMm),
                2
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
                            TextStyle.Default.FontFamily("Arial").FontSize(compactLabel ? 7f : 8.5f)
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

        [SupportedOSPlatform("windows")]
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
                2
            );

            var pdfBytes = Document
                .Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(ZebraLabelWidthMm, ZebraLabelHeightMm, Unit.Millimetre);
                        page.Margin(3f, Unit.Millimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(TextStyle.Default.FontFamily("Arial"));

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

        private static ZebraLabelContent BuildZebraLabelContent(Permit permit)
        {
            return new ZebraLabelContent(
                permit.PublicPermitCode,
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

        private string BuildPermitVerificationUrl(string token)
        {
            return BuildPermitPublicUrl(nameof(Verify), new { token });
        }

        private string BuildPermitQrContent(Permit permit)
        {
            if (!string.IsNullOrWhiteSpace(permit.QrToken))
            {
                var verificationUrl = BuildPermitVerificationUrl(permit.QrToken);
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
                var verificationUrl = BuildPermitVerificationUrl(persistedToken);
                if (!string.IsNullOrWhiteSpace(verificationUrl))
                {
                    return verificationUrl;
                }

                return persistedToken;
            }

            return string.Empty;
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

        private static PermitVerificationViewModel BuildPermitVerificationModel(
            Permit? permit,
            bool isAuthorized,
            string status,
            string message
        )
        {
            var model = new PermitVerificationViewModel
            {
                Permit = permit,
                IsValid = permit != null,
                IsAuthorized = isAuthorized,
                IsExpired = string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase),
                Message = message,
            };

            if (isAuthorized)
            {
                model.Title = "تصريح مصرح";
                model.StatusText = "مصرح";
                model.BadgeClass = "bg-success";
            }
            else if (string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase))
            {
                model.Title = "تصريح منتهي";
                model.StatusText = "منتهي";
                model.BadgeClass = "bg-warning text-dark";
            }
            else
            {
                model.Title = "تصريح غير مصرح";
                model.StatusText = "غير مصرح";
                model.BadgeClass = "bg-danger";
            }

            return model;
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

        [SupportedOSPlatform("windows")]
        private static byte[] RenderBarcodeImage(
            string content,
            BarcodeFormat format,
            int width,
            int height,
            int margin = 0
        )
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = format,
                Options = new EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = margin,
                    PureBarcode = true,
                },
            };

            var pixelData = writer.Write(content);
            using var bitmap = new Bitmap(
                pixelData.Width,
                pixelData.Height,
                PixelFormat.Format32bppRgb
            );
            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppRgb
            );

            try
            {
                Marshal.Copy(pixelData.Pixels, 0, bitmapData.Scan0, pixelData.Pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
    }
}
