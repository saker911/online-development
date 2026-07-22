using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
    public class AdministrationController : Controller
    {
        private readonly IUserAdminService _userAdminService;
        private readonly BackupService _backupService;
        private readonly IWebHostEnvironment _environment;
        private readonly ISystemClock _systemClock;
        private readonly IDisplayDeviceService? _displayDeviceService;

        public AdministrationController(
            IUserAdminService userAdminService,
            BackupService backupService,
            IWebHostEnvironment environment,
            ISystemClock systemClock,
            IDisplayDeviceService? displayDeviceService = null
        )
        {
            _userAdminService = userAdminService;
            _backupService = backupService;
            _environment = environment;
            _systemClock = systemClock;
            _displayDeviceService = displayDeviceService;
        }

        public IActionResult Index()
        {
            if (User.HasPermission(AppPermissions.ManageAdministration))
            {
                return RedirectToAction(nameof(Edit));
            }

            if (User.HasPermission(AppPermissions.ManageDepartments))
            {
                return RedirectToAction(nameof(Leadership));
            }

            return RedirectToAction("AccessDenied", "Home");
        }

        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult Edit()
        {
            var settings = _userAdminService.GetAdministrationSettings();
            return View(settings);
        }

        [Authorize(Policy = AppPolicies.ManageDepartments)]
        public IActionResult Departments(
            int? id = null,
            int? transferFromId = null,
            int? managerDepartmentId = null,
            int? handoverDepartmentId = null,
            int? handoverWizardStep = null,
            bool? handoverCreateNewManager = null,
            string? handoverExistingManagerUsername = null,
            string? handoverNewManagerUsername = null,
            string? handoverNewManagerFullName = null,
            string? handoverNewManagerPhoneNumber = null,
            string? handoverAssignmentType = null,
            string? handoverExitAction = null,
            string? handoverPreviousManagerNewRole = null,
            bool openGeneralManagerWizard = false,
            int? generalManagerWizardStep = null,
            string? generalManagerSelectionMode = null,
            string? generalManagerNewUserUsername = null,
            string? generalManagerNewUserFullName = null,
            string? generalManagerNewUserPhoneNumber = null,
            string? generalManagerNewUserJobTitle = null,
            string? generalManagerAssignmentType = null,
            string? generalManagerPreviousAction = null
        )
        {
            return Leadership(
                id,
                transferFromId,
                managerDepartmentId,
                handoverDepartmentId,
                handoverWizardStep,
                handoverCreateNewManager,
                handoverExistingManagerUsername,
                handoverNewManagerUsername,
                handoverNewManagerFullName,
                handoverNewManagerPhoneNumber,
                handoverAssignmentType,
                handoverExitAction,
                handoverPreviousManagerNewRole,
                openGeneralManagerWizard,
                generalManagerWizardStep,
                generalManagerSelectionMode,
                generalManagerNewUserUsername,
                generalManagerNewUserFullName,
                generalManagerNewUserPhoneNumber,
                generalManagerNewUserJobTitle,
                generalManagerAssignmentType,
                generalManagerPreviousAction
            );
        }

        [Authorize(Policy = AppPolicies.ManageDepartments)]
        public IActionResult Leadership(
            int? id = null,
            int? transferFromId = null,
            int? managerDepartmentId = null,
            int? handoverDepartmentId = null,
            int? handoverWizardStep = null,
            bool? handoverCreateNewManager = null,
            string? handoverExistingManagerUsername = null,
            string? handoverNewManagerUsername = null,
            string? handoverNewManagerFullName = null,
            string? handoverNewManagerPhoneNumber = null,
            string? handoverAssignmentType = null,
            string? handoverExitAction = null,
            string? handoverPreviousManagerNewRole = null,
            bool openGeneralManagerWizard = false,
            int? generalManagerWizardStep = null,
            string? generalManagerSelectionMode = null,
            string? generalManagerNewUserUsername = null,
            string? generalManagerNewUserFullName = null,
            string? generalManagerNewUserPhoneNumber = null,
            string? generalManagerNewUserJobTitle = null,
            string? generalManagerAssignmentType = null,
            string? generalManagerPreviousAction = null
        )
        {
            var department = id.HasValue ? _userAdminService.GetDepartment(id.Value) : null;
            ViewData["OpenCreatePanel"] = id.HasValue;
            ViewData["OpenTransferPanel"] = transferFromId.HasValue;
            ViewData["OpenManagerHandoverModal"] = handoverDepartmentId.HasValue;
            ViewData["OpenGeneralManagerWizard"] = openGeneralManagerWizard;

            DepartmentManagerHandoverRequest? handoverSeed = null;
            if (handoverDepartmentId.HasValue)
            {
                handoverSeed = new DepartmentManagerHandoverRequest
                {
                    DepartmentId = handoverDepartmentId.Value,
                    WizardStep = handoverWizardStep.GetValueOrDefault(1),
                    CreateNewManager = handoverCreateNewManager.GetValueOrDefault(),
                    ExistingManagerUsername = handoverExistingManagerUsername ?? string.Empty,
                    NewManagerUsername = handoverNewManagerUsername ?? string.Empty,
                    NewManagerFullName = handoverNewManagerFullName ?? string.Empty,
                    NewManagerPhoneNumber = handoverNewManagerPhoneNumber ?? string.Empty,
                    AssignmentType = string.IsNullOrWhiteSpace(handoverAssignmentType)
                        ? DepartmentManagerTransitionTypes.Permanent
                        : handoverAssignmentType,
                    ExitAction = string.IsNullOrWhiteSpace(handoverExitAction)
                        ? DepartmentManagerExitActions.EndAssignment
                        : handoverExitAction,
                    PreviousManagerNewRole = string.IsNullOrWhiteSpace(
                        handoverPreviousManagerNewRole
                    )
                        ? AppRoles.Employee
                        : handoverPreviousManagerNewRole,
                };
            }

            GeneralManagerAssignmentRequest? generalManagerSeed = null;
            if (openGeneralManagerWizard)
            {
                generalManagerSeed = new GeneralManagerAssignmentRequest
                {
                    WizardStep = generalManagerWizardStep.GetValueOrDefault(1),
                    SelectionMode = string.IsNullOrWhiteSpace(generalManagerSelectionMode)
                        ? GeneralManagerSelectionModes.ExistingUser
                        : generalManagerSelectionMode,
                    NewUserUsername = generalManagerNewUserUsername ?? string.Empty,
                    NewUserFullName = generalManagerNewUserFullName ?? string.Empty,
                    NewUserPhoneNumber = generalManagerNewUserPhoneNumber ?? string.Empty,
                    NewUserJobTitle = generalManagerNewUserJobTitle ?? string.Empty,
                    NewUserIsActive = true,
                    AssignmentType = string.IsNullOrWhiteSpace(generalManagerAssignmentType)
                        ? GeneralManagerAssignmentTypes.Permanent
                        : generalManagerAssignmentType,
                    PreviousGeneralManagerAction = string.IsNullOrWhiteSpace(
                        generalManagerPreviousAction
                    )
                        ? GeneralManagerPreviousActions.EndAssignment
                        : generalManagerPreviousAction,
                };
            }

            return View(
                nameof(Departments),
                BuildDepartmentManagementViewModel(
                    department,
                    transferFromId,
                    managerDepartmentId,
                    handoverDepartmentId,
                    handoverSeed,
                    generalManagerSeed
                )
            );
        }

        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult DisplaySettings()
        {
            return View(BuildDisplaySettingsViewModel());
        }

        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult DisplayDevices()
        {
            return RedirectToAction(nameof(DisplaySettings));
        }

        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult DisplayDeviceDetails(int id)
        {
            var device = _displayDeviceService!.GetDeviceById(id);
            return device == null ? NotFound() : View(device);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult ApproveDisplayDevice(int id)
        {
            var result = _displayDeviceService!.ApproveDevice(
                id,
                User.Identity?.Name ?? string.Empty
            );
            this.ToastSuccess(
                result.Success
                    ? "تم اعتماد شاشة العرض. سيستلم الجهاز الرمز تلقائيًا عند اتصاله."
                    : "تعذر اعتماد شاشة العرض."
            );
            return RedirectToAction(nameof(DisplaySettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult RejectDisplayDevice(int id)
        {
            this.ToastSuccess(
                _displayDeviceService!.RejectDevice(id, User.Identity?.Name ?? string.Empty)
                    ? "تم رفض شاشة العرض."
                    : "تعذر رفض شاشة العرض."
            );
            return RedirectToAction(nameof(DisplaySettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult DisableDisplayDevice(int id)
        {
            this.ToastSuccess(
                _displayDeviceService!.DisableDevice(id, User.Identity?.Name ?? string.Empty)
                    ? "تم تعطيل شاشة العرض."
                    : "تعذر تعطيل شاشة العرض."
            );
            return RedirectToAction(nameof(DisplaySettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult ActivateDisplayDevice(int id)
        {
            this.ToastSuccess(
                _displayDeviceService!.ActivateDevice(id, User.Identity?.Name ?? string.Empty)
                    ? "تم تنشيط شاشة العرض."
                    : "تعذر تنشيط شاشة العرض."
            );
            return RedirectToAction(nameof(DisplaySettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult DeleteDisplayDevice(int id)
        {
            this.ToastSuccess(
                _displayDeviceService!.DeleteDevice(id, User.Identity?.Name ?? string.Empty)
                    ? "تم حذف شاشة العرض."
                    : "تعذر حذف شاشة العرض."
            );
            return RedirectToAction(nameof(DisplaySettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult RotateDisplayDeviceToken(int id)
        {
            var result = _displayDeviceService!.RotateDeviceToken(
                id,
                User.Identity?.Name ?? string.Empty
            );
            this.ToastSuccess(
                result.Success
                    ? "تم تدوير رمز الجهاز. سيستلم جهاز العرض الرمز الجديد عند اتصاله."
                    : "تعذر تدوير رمز الجهاز."
            );
            return RedirectToAction(nameof(DisplaySettings));
        }

        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult Backup()
        {
            return View(BuildBackupDashboardViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageDepartments)]
        public IActionResult Departments(DepartmentManagementViewModel model)
        {
            AdministrationRequestNormalizer.NormalizeDepartment(model.Department);

            var isEditing = model.Department.Id > 0;
            model.Department.ManagerDisplayName = string.Empty;
            if (!isEditing)
            {
                model.Department.ManagerUsername = string.Empty;
                model.Department.IsActive = true;
            }

            if (!ModelState.IsValid)
            {
                ViewData["OpenCreatePanel"] = true;
                return View(BuildDepartmentManagementViewModel(model.Department));
            }

            var duplicateDepartmentExists = _userAdminService
                .GetDepartments()
                .Any(department =>
                    string.Equals(
                        department.Name,
                        model.Department.Name,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && department.Id != model.Department.Id
                );

            if (duplicateDepartmentExists)
            {
                ModelState.AddModelError(string.Empty, "القسم موجود مسبقًا، اختر اسمًا آخر.");
                ViewData["OpenCreatePanel"] = true;
                return View(BuildDepartmentManagementViewModel(model.Department));
            }

            if (_userAdminService.UpsertDepartment(model.Department))
            {
                this.ToastSuccess("تم حفظ القسم بنجاح.");
            }
            else
            {
                this.ToastError("تعذر حفظ القسم.");
            }

            return RedirectToAction(nameof(Leadership));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageDepartments)]
        public IActionResult AssignDepartmentManager(DepartmentManagementViewModel model)
        {
            if (model.ManagerDepartmentId <= 0)
            {
                this.ToastError("يرجى اختيار قسم صالح أولًا.");
                return RedirectToAction(nameof(Leadership));
            }

            var department = _userAdminService.GetDepartment(model.ManagerDepartmentId);
            var previousManagerUsername = department?.ManagerUsername?.Trim() ?? string.Empty;
            var previousManagerName = string.IsNullOrWhiteSpace(department?.ManagerDisplayName)
                ? previousManagerUsername
                : department!.ManagerDisplayName;
            var nextManagerUsername = (model.ManagerUsername ?? string.Empty).Trim();
            var nextManagerAccount = string.IsNullOrWhiteSpace(nextManagerUsername)
                ? null
                : _userAdminService.GetUserAccount(nextManagerUsername);
            var nextManagerName = nextManagerAccount?.DisplayName ?? nextManagerUsername;

            if (
                nextManagerAccount != null
                && !string.Equals(
                    nextManagerAccount.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                this.ToastError(
                    "المدير العام يشرف على جميع الأقسام ولا يمكن تعيينه مديرًا لقسم. اختر مستخدمًا بدور مدير."
                );
                return RedirectToAction(
                    nameof(Leadership),
                    new { managerDepartmentId = model.ManagerDepartmentId }
                );
            }

            if (
                _userAdminService.SetDepartmentManager(
                    model.ManagerDepartmentId,
                    model.ManagerUsername
                )
            )
            {
                string message;
                if (department == null)
                {
                    message = "تم تعيين مدير القسم بنجاح.";
                }
                else if (string.IsNullOrWhiteSpace(nextManagerUsername))
                {
                    message = string.IsNullOrWhiteSpace(previousManagerUsername)
                        ? $"تم حفظ قسم {department.Name} بدون مدير."
                        : $"تمت إزالة المدير {previousManagerName} من قسم {department.Name}.";
                }
                else if (string.IsNullOrWhiteSpace(previousManagerUsername))
                {
                    message = $"تم تعيين {nextManagerName} مديرًا لقسم {department.Name}.";
                }
                else if (
                    string.Equals(
                        previousManagerUsername,
                        nextManagerUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    message = $"تم تثبيت {nextManagerName} مديرًا لقسم {department.Name}.";
                }
                else
                {
                    message =
                        $"تم استبدال مدير قسم {department.Name} من {previousManagerName} إلى {nextManagerName}.";
                }

                this.ToastSuccess(message);
            }
            else
            {
                this.ToastError("تعذر تعيين مدير القسم.");
            }

            return RedirectToAction(
                nameof(Leadership),
                new { managerDepartmentId = model.ManagerDepartmentId }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageDepartments)]
        public IActionResult HandoverDepartmentManager(
            [Bind(Prefix = "Handover")] DepartmentManagerHandoverRequest request
        )
        {
            if (!User.IsSuperAdmin())
            {
                this.ToastError("نقل المدير الحالي متاح فقط لمالك النظام.");
                return RedirectToAction(nameof(Leadership));
            }

            AdministrationRequestNormalizer.NormalizeDepartmentManagerHandover(request);

            var department =
                request.DepartmentId > 0
                    ? _userAdminService.GetDepartment(request.DepartmentId)
                    : null;
            if (department == null)
            {
                ModelState.AddModelError(
                    HandoverModelKey(nameof(request.DepartmentId)),
                    "اختر قسمًا صالحًا لتنفيذ تسليم المدير."
                );
            }
            else if (string.IsNullOrWhiteSpace(department.ManagerUsername))
            {
                ModelState.AddModelError(
                    HandoverModelKey(nameof(request.DepartmentId)),
                    "القسم المحدد غير مرتبط حاليًا بمدير لنقله."
                );
            }

            if (request.CreateNewManager)
            {
                if (!SaudiNationalIdOrIqamaValidator.IsValid(request.NewManagerUsername))
                {
                    ModelState.AddModelError(
                        HandoverModelKey(nameof(request.NewManagerUsername)),
                        SaudiNationalIdOrIqamaValidator.ErrorMessage
                    );
                }

                if (string.IsNullOrWhiteSpace(request.NewManagerFullName))
                {
                    ModelState.AddModelError(
                        HandoverModelKey(nameof(request.NewManagerFullName)),
                        "اسم المدير الجديد مطلوب."
                    );
                }

                if (!SaudiMobileNumberValidator.IsValidRequired(request.NewManagerPhoneNumber))
                {
                    ModelState.AddModelError(
                        HandoverModelKey(nameof(request.NewManagerPhoneNumber)),
                        SaudiMobileNumberValidator.ErrorMessage
                    );
                }
            }
            else if (string.IsNullOrWhiteSpace(request.ExistingManagerUsername))
            {
                ModelState.AddModelError(
                    HandoverModelKey(nameof(request.ExistingManagerUsername)),
                    "اختر المدير الجديد من القائمة أو فعّل خيار إنشاء مدير جديد."
                );
            }

            var normalizedExitAction = request.ExitAction;
            if (
                !string.Equals(
                    normalizedExitAction,
                    DepartmentManagerExitActions.InternalTransfer,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                request.PreviousManagerTargetDepartmentId = 0;
                if (
                    string.Equals(
                        request.PreviousManagerNewRole,
                        AppRoles.DepartmentManager,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    ModelState.AddModelError(
                        HandoverModelKey(nameof(request.PreviousManagerNewRole)),
                        "يمكن إبقاء المدير السابق بدور مدير فقط عند نقله داخليًا إلى قسم آخر بلا مدير."
                    );
                }
            }
            else
            {
                if (request.PreviousManagerTargetDepartmentId <= 0)
                {
                    ModelState.AddModelError(
                        HandoverModelKey(nameof(request.PreviousManagerTargetDepartmentId)),
                        "اختر القسم الهدف للمدير السابق عند النقل الداخلي."
                    );
                }
                else if (
                    department != null
                    && request.PreviousManagerTargetDepartmentId == department.Id
                )
                {
                    ModelState.AddModelError(
                        HandoverModelKey(nameof(request.PreviousManagerTargetDepartmentId)),
                        "القسم الهدف للنقل الداخلي يجب أن يختلف عن القسم الحالي."
                    );
                }
            }

            if (
                string.Equals(
                    request.PreviousManagerNewRole,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ModelState.AddModelError(
                    HandoverModelKey(nameof(request.PreviousManagerNewRole)),
                    "لا يمكن تحويل المدير السابق مباشرة إلى دور مدير عام من هذه الشاشة."
                );
            }

            if (!ModelState.IsValid)
            {
                request.WizardStep =
                    AdministrationWizardStepResolver.ResolveManagerHandoverWizardStep(
                        ModelState,
                        request.WizardStep
                    );
                return View(nameof(Departments), BuildInvalidManagerHandoverModel(request));
            }

            var message = _userAdminService.HandoverDepartmentManager(request, User.Identity?.Name);
            if (string.IsNullOrWhiteSpace(message))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "تعذر تنفيذ نقل المدير الحالي. تحقق من صلاحية المدير الجديد وحالة المدير السابق والبيانات المرافقة."
                );
                request.WizardStep = 3;
                return View(nameof(Departments), BuildInvalidManagerHandoverModel(request));
            }

            this.ToastSuccess(message);
            return RedirectToAction(nameof(Leadership));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageDepartments)]
        public IActionResult AssignGeneralManager(DepartmentManagementViewModel model)
        {
            var request = model.GeneralManagerAssignment ?? new GeneralManagerAssignmentRequest();
            GeneralManagerAssignmentMapper.ApplyFormValues(Request, request);
            ClearModelStateExceptPrefix(
                nameof(DepartmentManagementViewModel.GeneralManagerAssignment)
            );

            if (!User.IsSuperAdmin())
            {
                this.ToastError("تعيين أو تغيير المدير العام متاح فقط لمالك النظام.");
                return RedirectToAction(nameof(Leadership));
            }

            GeneralManagerAssignmentMapper.Normalize(request);

            var currentSettings = _userAdminService.GetAdministrationSettings();
            var hasCurrentGeneralManager = !string.IsNullOrWhiteSpace(
                currentSettings.GeneralManagerUsername
            );
            var normalizedSelectionMode = string.Equals(
                request.SelectionMode,
                GeneralManagerSelectionModes.CreateNew,
                StringComparison.OrdinalIgnoreCase
            )
                ? GeneralManagerSelectionModes.CreateNew
                : GeneralManagerSelectionModes.ExistingUser;
            var normalizedPreviousAction =
                GeneralManagerAssignmentMapper.NormalizePreviousActionValue(
                    request.PreviousGeneralManagerAction
                );

            if (
                normalizedSelectionMode == GeneralManagerSelectionModes.ExistingUser
                && string.IsNullOrWhiteSpace(request.ExistingUserUsername)
            )
            {
                ModelState.AddModelError(
                    GeneralManagerModelKey(nameof(request.ExistingUserUsername)),
                    "اختر المستخدم المرشح لتولي منصب المدير العام."
                );
            }

            if (normalizedSelectionMode == GeneralManagerSelectionModes.CreateNew)
            {
                if (!SaudiNationalIdOrIqamaValidator.IsValid(request.NewUserUsername))
                {
                    ModelState.AddModelError(
                        GeneralManagerModelKey(nameof(request.NewUserUsername)),
                        SaudiNationalIdOrIqamaValidator.ErrorMessage
                    );
                }

                if (string.IsNullOrWhiteSpace(request.NewUserFullName))
                {
                    ModelState.AddModelError(
                        GeneralManagerModelKey(nameof(request.NewUserFullName)),
                        "الاسم الكامل مطلوب."
                    );
                }

                if (!SaudiMobileNumberValidator.IsValidRequired(request.NewUserPhoneNumber))
                {
                    ModelState.AddModelError(
                        GeneralManagerModelKey(nameof(request.NewUserPhoneNumber)),
                        SaudiMobileNumberValidator.ErrorMessage
                    );
                }

                if (string.IsNullOrWhiteSpace(request.NewUserPassword))
                {
                    ModelState.AddModelError(
                        GeneralManagerModelKey(nameof(request.NewUserPassword)),
                        "كلمة المرور مطلوبة."
                    );
                }
                else if (!PasswordValidationRules.IsStrongPassword(request.NewUserPassword))
                {
                    ModelState.AddModelError(
                        GeneralManagerModelKey(nameof(request.NewUserPassword)),
                        "كلمة المرور يجب أن تكون 8 أحرف على الأقل وتحتوي على حرف كبير وحرف صغير ورقم ورمز خاص."
                    );
                }

                if (
                    string.IsNullOrWhiteSpace(request.NewUserConfirmPassword)
                    || !string.Equals(
                        request.NewUserPassword,
                        request.NewUserConfirmPassword,
                        StringComparison.Ordinal
                    )
                )
                {
                    ModelState.AddModelError(
                        GeneralManagerModelKey(nameof(request.NewUserConfirmPassword)),
                        "تأكيد كلمة المرور غير مطابق."
                    );
                }

                if (string.IsNullOrWhiteSpace(request.NewUserJobTitle))
                {
                    ModelState.AddModelError(
                        GeneralManagerModelKey(nameof(request.NewUserJobTitle)),
                        "المسمى الوظيفي مطلوب."
                    );
                }

                request.NewUserDepartmentId = 0;
            }

            if (
                hasCurrentGeneralManager
                && string.Equals(
                    normalizedPreviousAction,
                    GeneralManagerPreviousActions.InternalTransfer,
                    StringComparison.OrdinalIgnoreCase
                )
                && request.PreviousGeneralManagerTargetDepartmentId <= 0
            )
            {
                ModelState.AddModelError(
                    GeneralManagerModelKey(
                        nameof(request.PreviousGeneralManagerTargetDepartmentId)
                    ),
                    "اختر القسم الجديد للمدير العام السابق عند النقل الداخلي."
                );
            }

            if (!hasCurrentGeneralManager)
            {
                request.PreviousGeneralManagerTargetDepartmentId = 0;
            }

            if (!ModelState.IsValid)
            {
                request.WizardStep =
                    AdministrationWizardStepResolver.ResolveGeneralManagerWizardStep(
                        ModelState,
                        request.WizardStep
                    );
                return View(nameof(Departments), BuildInvalidGeneralManagerModel(request));
            }

            var message = _userAdminService.AssignGeneralManager(request, User.Identity?.Name);
            if (string.IsNullOrWhiteSpace(message))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "تعذر تنفيذ تعيين أو تغيير المدير العام. تحقق من المستخدم المختار وإجراء المدير السابق والبيانات المدخلة."
                );
                request.WizardStep = 5;
                return View(nameof(Departments), BuildInvalidGeneralManagerModel(request));
            }

            this.ToastSuccess(message);
            return RedirectToAction(nameof(Leadership));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageDepartments)]
        public IActionResult TransferDepartmentUsers(DepartmentManagementViewModel model)
        {
            if (model.TransferSourceDepartmentId <= 0 || model.TransferTargetDepartmentId <= 0)
            {
                ModelState.AddModelError(string.Empty, "اختر القسم الحالي والقسم الهدف أولًا.");
                return View(nameof(Departments), BuildInvalidTransferModel(model));
            }

            if (model.TransferSourceDepartmentId == model.TransferTargetDepartmentId)
            {
                ModelState.AddModelError(string.Empty, "القسم الهدف يجب أن يختلف عن القسم الحالي.");
                return View(nameof(Departments), BuildInvalidTransferModel(model));
            }

            var sourceDepartment = _userAdminService.GetDepartment(
                model.TransferSourceDepartmentId
            );
            var targetDepartment = _userAdminService.GetDepartment(
                model.TransferTargetDepartmentId
            );
            if (sourceDepartment == null || targetDepartment == null)
            {
                this.ToastError("تعذر العثور على أحد الأقسام المحددة.");
                return RedirectToAction(nameof(Leadership));
            }

            if (!model.TransferAllUsers && !model.SelectedTransferUsernames.Any())
            {
                ModelState.AddModelError(
                    string.Empty,
                    "حدد موظفًا واحدًا على الأقل أو اختر نقل الجميع."
                );
                return View(nameof(Departments), BuildInvalidTransferModel(model));
            }

            var sourceManagerUsername = (sourceDepartment.ManagerUsername ?? string.Empty).Trim();
            var replacementManagerUsername = (
                model.TransferReplacementManagerUsername ?? string.Empty
            ).Trim();
            var isReplacementManagerInSourceDepartment =
                !string.IsNullOrWhiteSpace(replacementManagerUsername)
                && _userAdminService
                    .GetUsersByDepartment(sourceDepartment.Name)
                    .Any(user =>
                        string.Equals(
                            user.Username,
                            replacementManagerUsername,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
            var isSourceManagerSelectedForTransfer =
                !string.IsNullOrWhiteSpace(sourceManagerUsername)
                && (
                    model.TransferAllUsers
                    || model.SelectedTransferUsernames.Any(username =>
                        string.Equals(
                            username,
                            sourceManagerUsername,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                );

            if (
                isSourceManagerSelectedForTransfer
                && string.IsNullOrWhiteSpace(replacementManagerUsername)
            )
            {
                ModelState.AddModelError(
                    nameof(model.TransferReplacementManagerUsername),
                    "لا يمكن نقل مدير القسم الحالي دون اختيار بديل له أولًا."
                );
                return View(nameof(Departments), BuildInvalidTransferModel(model));
            }

            if (
                isSourceManagerSelectedForTransfer
                && string.Equals(
                    replacementManagerUsername,
                    sourceManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ModelState.AddModelError(
                    nameof(model.TransferReplacementManagerUsername),
                    "اختر مديرًا بديلًا مختلفًا عن المدير الحالي قبل تنفيذ النقل."
                );
                return View(nameof(Departments), BuildInvalidTransferModel(model));
            }

            if (
                isSourceManagerSelectedForTransfer
                && (
                    (model.TransferAllUsers && isReplacementManagerInSourceDepartment)
                    || model.SelectedTransferUsernames.Any(username =>
                        string.Equals(
                            username,
                            replacementManagerUsername,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
            )
            {
                ModelState.AddModelError(
                    nameof(model.TransferReplacementManagerUsername),
                    "لا يمكن اختيار مدير بديل ضمن الموظفين المنقولين في العملية نفسها."
                );
                return View(nameof(Departments), BuildInvalidTransferModel(model));
            }

            if (
                _userAdminService.TransferDepartmentUsers(
                    sourceDepartment.Name,
                    targetDepartment.Name,
                    model.SelectedTransferUsernames,
                    model.TransferAllUsers,
                    User.Identity?.Name,
                    replacementManagerUsername
                )
            )
            {
                var replacementManagerName = string.IsNullOrWhiteSpace(replacementManagerUsername)
                    ? string.Empty
                    : _userAdminService.GetUserAccount(replacementManagerUsername)?.DisplayName
                        ?? replacementManagerUsername;
                var message = model.TransferAllUsers
                    ? $"تم نقل جميع موظفي {sourceDepartment.Name} إلى {targetDepartment.Name}."
                    : $"تم نقل الموظفين المحددين من {sourceDepartment.Name} إلى {targetDepartment.Name}.";

                if (
                    isSourceManagerSelectedForTransfer
                    && !string.IsNullOrWhiteSpace(replacementManagerName)
                )
                {
                    message =
                        $"{message} وتم تعيين {replacementManagerName} مديرًا جديدًا لقسم {sourceDepartment.Name}.";
                }

                this.ToastSuccess(message);
            }
            else
            {
                this.ToastError("تعذر تنفيذ النقل. تأكد من اختيار موظفين صالحين وقسم هدف مختلف.");
            }

            return RedirectToAction(
                nameof(Leadership),
                new { transferFromId = model.TransferSourceDepartmentId }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageDepartments)]
        public IActionResult DeleteDepartment(int id)
        {
            if (_userAdminService.DeleteDepartment(id))
            {
                this.ToastSuccess("تم حذف القسم بنجاح.");
            }
            else
            {
                var blockReason = _userAdminService.GetDepartmentDeleteBlockReason(id);
                this.ToastError(
                    string.IsNullOrWhiteSpace(blockReason)
                        ? "لا يمكن حذف القسم لأنه غير موجود أو هناك خطأ غير متوقع."
                        : $"لا يمكن حذف القسم لأنه {blockReason}."
                );
            }

            return RedirectToAction(nameof(Leadership));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public async Task<IActionResult> CreateBackup()
        {
            var backup = await _backupService.CreateBackupAsync(User.Identity?.Name);
            if (backup == null)
            {
                this.ToastWarning(
                    "تعذر إنشاء النسخة الاحتياطية لأن الخدمة غير مفعلة أو أن مصدر البيانات الحالي لا يدعم النسخ."
                );
            }
            else
            {
                this.ToastSuccess($"تم إنشاء النسخة الاحتياطية: {backup.FileName}");
            }

            return RedirectToAction(nameof(Backup));
        }

        [HttpGet]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult DownloadBackup(string fileName)
        {
            var safeFileName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                return NotFound();
            }

            var fullPath = Path.Combine(_backupService.BackupRootPath, safeFileName);
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound();
            }

            return PhysicalFile(fullPath, "application/zip", safeFileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public async Task<IActionResult> RestoreBackup(IFormFile? backupFile)
        {
            if (backupFile == null || backupFile.Length == 0)
            {
                ModelState.AddModelError(string.Empty, "يرجى اختيار ملف نسخة احتياطية صالح.");
                return View(nameof(Backup), BuildBackupDashboardViewModel());
            }

            if (backupFile.Length > BackupService.MaxRestoreArchiveBytes)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "حجم ملف النسخة الاحتياطية يتجاوز الحد المسموح (100 ميجابايت)."
                );
                return View(nameof(Backup), BuildBackupDashboardViewModel());
            }

            var extension = Path.GetExtension(backupFile.FileName);
            if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "يجب أن يكون الملف بصيغة ZIP.");
                return View(nameof(Backup), BuildBackupDashboardViewModel());
            }

            try
            {
                await _backupService.RestoreBackupAsync(backupFile);
                this.ToastSuccess("تمت استعادة النسخة الاحتياطية بنجاح.");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(
                    string.Empty,
                    $"تعذر استعادة النسخة الاحتياطية: {BuildRestoreBackupErrorMessage(ex)}"
                );
                return View(nameof(Backup), BuildBackupDashboardViewModel());
            }

            return RedirectToAction(nameof(Backup));
        }

        private static string BuildRestoreBackupErrorMessage(Exception exception)
        {
            var message = exception.Message;
            if (
                message.Contains(
                    "does not contain a database file",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "ملف ZIP لا يحتوي على قاعدة بيانات قابلة للاسترجاع.";
            }

            if (
                message.Contains("not valid", StringComparison.OrdinalIgnoreCase)
                || message.Contains("could not be verified", StringComparison.OrdinalIgnoreCase)
            )
            {
                return "فشل فحص سلامة قاعدة البيانات داخل النسخة، ولم يتم اعتماد الاسترجاع.";
            }

            if (message.Contains("empty", StringComparison.OrdinalIgnoreCase))
            {
                return "ملف النسخة الاحتياطية فارغ أو غير مكتمل.";
            }

            if (message.Contains("unsafe file path", StringComparison.OrdinalIgnoreCase))
            {
                return "تم رفض ملف النسخة لأنه يحتوي على مسارات غير آمنة.";
            }

            return "راجع ملف النسخة وتأكد أنه ZIP صادر من صفحة النسخ الاحتياطي.";
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public async Task<IActionResult> Edit(
            AdministrationSettings settings,
            IFormFile? logoFile,
            IFormFile? signatureFile,
            bool removeLogo = false
        )
        {
            var currentSettings = _userAdminService.GetAdministrationSettings();
            var effectiveSettings = BuildAdministrationEditSettings(settings, currentSettings);

            if (!ModelState.IsValid)
            {
                return View(effectiveSettings);
            }

            try
            {
                var previousLogoPath = effectiveSettings.LogoPath;
                if (removeLogo)
                {
                    effectiveSettings.LogoPath = null;
                }

                if (logoFile is { Length: > 0 })
                {
                    effectiveSettings.LogoPath = await AdministrationImageStorage.SaveAsync(
                        logoFile,
                        "logo",
                        _systemClock.UtcNow
                    );
                }

                if (signatureFile is { Length: > 0 })
                {
                    var processedSignature = await AdministrationImageStorage.ProcessAsync(
                        signatureFile
                    );
                    effectiveSettings.SignatureImageData = processedSignature.Data;
                    effectiveSettings.SignatureImageContentType = processedSignature.ContentType;
                    effectiveSettings.SignatureImagePath = null;
                }

                _userAdminService.UpdateAdministrationSettings(effectiveSettings);

                if (
                    !string.Equals(
                        previousLogoPath,
                        effectiveSettings.LogoPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    AdministrationImageStorage.DeleteIfManaged(previousLogoPath);
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(effectiveSettings);
            }

            this.ToastSuccess("تم تحديث بيانات الإدارة بنجاح");
            return RedirectToAction(nameof(Edit));
        }

        [HttpGet]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult SignaturePreview()
        {
            var settings = _userAdminService.GetAdministrationSettings();
            Response.Headers.CacheControl = "no-store, private";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (settings.SignatureImageData is { Length: > 0 })
            {
                var contentType = string.IsNullOrWhiteSpace(settings.SignatureImageContentType)
                    ? "image/png"
                    : settings.SignatureImageContentType;
                return File(settings.SignatureImageData, contentType);
            }

            var path = settings.SignatureImagePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return NotFound();
            }

            var physicalPath = AppStoragePaths.ResolveUploadPhysicalPath(path);
            if (!System.IO.File.Exists(physicalPath))
            {
                return NotFound();
            }

            var fallbackContentType = Path.GetExtension(physicalPath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "image/png",
            };
            return PhysicalFile(physicalPath, fallbackContentType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult DisplaySettings(DisplaySettingsViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.DisplayBaseUrl))
            {
                ModelState.Remove(nameof(model.DisplayBaseUrl));
                model.DisplayBaseUrl = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(model.AllowedClientIpRanges))
            {
                ModelState.Remove(nameof(model.AllowedClientIpRanges));
                model.AllowedClientIpRanges = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(model.DisplayBaseUrl))
            {
                if (
                    !Uri.TryCreate(model.DisplayBaseUrl.Trim(), UriKind.Absolute, out var parsedUri)
                    || (
                        parsedUri.Scheme != Uri.UriSchemeHttp
                        && parsedUri.Scheme != Uri.UriSchemeHttps
                    )
                )
                {
                    ModelState.AddModelError(
                        nameof(model.DisplayBaseUrl),
                        "أدخل رابطًا صحيحًا مثل https://vehicle-server"
                    );
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var currentSettings = _userAdminService.GetAdministrationSettings();
            var settings = currentSettings;
            settings.DisplayBaseUrl = model.DisplayBaseUrl.Trim().TrimEnd('/');
            settings.DisplayAccessKey = currentSettings.DisplayAccessKey;
            settings.AllowedClientIpRanges = model.AllowedClientIpRanges.Trim();

            _userAdminService.UpdateAdministrationSettings(settings);
            this.ToastSuccess("تم تحديث إعدادات الشبكة وشاشات العرض بنجاح");
            return RedirectToAction(nameof(DisplaySettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageAdministration)]
        public IActionResult RotateDisplayAccessKey()
        {
            var settings = _userAdminService.GetAdministrationSettings();
            var accessKey = DisplayAccessDefaults.CreateAccessKey();
            settings.DisplayAccessKey = DisplayAccessKeyHasher.Hash(accessKey);
            _userAdminService.UpdateAdministrationSettings(settings);
            _displayDeviceService?.InvalidateDeviceTrust(User.Identity?.Name ?? string.Empty);
            TempData["NewDisplayAccessKey"] = accessKey;
            this.ToastWarning(
                "تم توليد مفتاح جديد. سيظهر مرة واحدة فقط في هذه الصفحة."
            );
            return RedirectToAction(nameof(DisplaySettings));
        }

        private DisplaySettingsViewModel BuildDisplaySettingsViewModel()
        {
            var settings = _userAdminService.GetAdministrationSettings();
            var oneTimeAccessKey = TempData["NewDisplayAccessKey"] as string ?? string.Empty;
            return new DisplaySettingsViewModel
            {
                Id = settings.Id,
                DisplayBaseUrl = settings.DisplayBaseUrl,
                DisplayAccessKey = oneTimeAccessKey,
                AllowedClientIpRanges = settings.AllowedClientIpRanges,
                GateDisplayUrl = BuildDisplayManagementUrl(
                    nameof(DisplayController.Gate),
                    includeSetupKey: false
                ),
                VisitsDisplayUrl = BuildDisplayManagementUrl(
                    nameof(DisplayController.Visits),
                    includeSetupKey: false
                ),
                RegistrationUrl = BuildDisplayManagementUrl(
                    nameof(DisplayController.Register),
                    oneTimeAccessKey
                ),
                MaskedDisplayAccessKey = "محفوظ بشكل آمن",
                DeviceManagement = _displayDeviceService?.BuildManagementViewModel() ?? new(),
            };
        }

        private string BuildDisplayManagementUrl(
            string actionName,
            string? oneTimeSetupKey = null,
            bool includeSetupKey = false
        )
        {
            var path = Url.Action(actionName, "Display") ?? $"/Display/{actionName}";
            var settings = _userAdminService.GetAdministrationSettings();
            var baseUrl = string.IsNullOrWhiteSpace(settings.DisplayBaseUrl)
                ? $"{Request.Scheme}://{Request.Host}"
                : settings.DisplayBaseUrl.TrimEnd('/');
            var url = $"{baseUrl}{path}";
            if (!string.IsNullOrWhiteSpace(oneTimeSetupKey))
            {
                url += $"#setupKey={Uri.EscapeDataString(oneTimeSetupKey)}";
            }

            return url;
        }

        private static string MaskSecret(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length <= 6)
            {
                return string.IsNullOrWhiteSpace(normalized) ? "غير مهيأ" : "******";
            }

            return $"••••••{normalized[^6..]}";
        }

        private static AdministrationSettings BuildAdministrationEditSettings(
            AdministrationSettings postedSettings,
            AdministrationSettings currentSettings
        )
        {
            return new AdministrationSettings
            {
                Id = currentSettings.Id,
                IsInitialSetupCompleted = currentSettings.IsInitialSetupCompleted,
                OrganizationName = postedSettings.OrganizationName,
                DepartmentName = postedSettings.DepartmentName,
                Address = postedSettings.Address,
                Phone = postedSettings.Phone,
                Email = postedSettings.Email,
                GeneralManagerUsername = currentSettings.GeneralManagerUsername,
                ManagerName = currentSettings.ManagerName,
                ManagerTitle = currentSettings.ManagerTitle,
                ManagerPhoneNumber = currentSettings.ManagerPhoneNumber,
                SignatureText = postedSettings.SignatureText,
                LogoPath = currentSettings.LogoPath,
                SignatureImagePath = currentSettings.SignatureImagePath,
                SignatureImageData = currentSettings.SignatureImageData,
                SignatureImageContentType = currentSettings.SignatureImageContentType,
                DisplayBaseUrl = currentSettings.DisplayBaseUrl,
                DisplayAccessKey = currentSettings.DisplayAccessKey,
                AllowedClientIpRanges = currentSettings.AllowedClientIpRanges,
                WorkStartTime = postedSettings.WorkStartTime,
                WorkEndTime = postedSettings.WorkEndTime,
                AttendanceGraceMinutes = postedSettings.AttendanceGraceMinutes,
                WorkEndExitGraceMinutes = postedSettings.WorkEndExitGraceMinutes,
                LateReturnGraceMinutes = postedSettings.LateReturnGraceMinutes,
                LeaveRequestsEnabled = postedSettings.LeaveRequestsEnabled,
                LastWorkEndClosureAt = currentSettings.LastWorkEndClosureAt,
                OfficialWorkDaysCsv = postedSettings.OfficialWorkDaysCsv,
            };
        }

        private DepartmentManagementViewModel BuildDepartmentManagementViewModel(
            Department? department = null,
            int? transferFromId = null,
            int? managerDepartmentId = null,
            int? handoverDepartmentId = null,
            DepartmentManagerHandoverRequest? handoverSeed = null,
            GeneralManagerAssignmentRequest? generalManagerSeed = null
        )
        {
            var administrationSettings = _userAdminService.GetAdministrationSettings();
            var departments = _userAdminService.GetDepartments().ToList();
            var administrationDepartment = departments.FirstOrDefault(x =>
                string.Equals(
                    x.Name,
                    administrationSettings.DepartmentName,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            var allUsers = _userAdminService.GetAllUsers().ToList();
            var currentGeneralManager = allUsers.FirstOrDefault(user =>
                string.Equals(
                    user.Username,
                    administrationSettings.GeneralManagerUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            var departmentUsersByDepartmentId = departments.ToDictionary(
                dept => dept.Id,
                dept =>
                    allUsers
                        .Where(user =>
                            string.Equals(
                                user.Department,
                                dept.Name,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .OrderBy(user => user.FullName)
                        .ThenBy(user => user.Username)
                        .ToList()
            );
            var userDepartmentsByUsername = allUsers.ToDictionary(
                user => user.Username,
                user => user.Department ?? string.Empty,
                StringComparer.OrdinalIgnoreCase
            );

            var transferSourceDepartment = transferFromId.HasValue
                ? departments.FirstOrDefault(x => x.Id == transferFromId.Value)
                : null;
            var selectedManagerDepartment = managerDepartmentId.HasValue
                ? departments.FirstOrDefault(x => x.Id == managerDepartmentId.Value)
                : department;
            var selectedHandoverDepartment = handoverDepartmentId.HasValue
                ? departments.FirstOrDefault(x => x.Id == handoverDepartmentId.Value)
                : department;
            var managerOptions = AdministrationCandidateOptionsBuilder.GetDepartmentManagerOptions(
                allUsers,
                selectedManagerDepartment
            );

            var departmentList =
                administrationDepartment == null
                    ? departments
                    : departments.Where(x => x.Id != administrationDepartment.Id).ToList();

            var handoverRequest = handoverSeed ?? new DepartmentManagerHandoverRequest();
            if (selectedHandoverDepartment != null)
            {
                handoverRequest.DepartmentId = selectedHandoverDepartment.Id;
            }

            return new DepartmentManagementViewModel
            {
                Department =
                    department != null
                        ? new Department
                        {
                            Id = department.Id,
                            Name = department.Name,
                            ManagerUsername = department.ManagerUsername,
                            ManagerDisplayName = department.ManagerDisplayName,
                            IsActive = department.IsActive,
                        }
                        : new Department(),
                AdministrationDepartment = administrationDepartment,
                CurrentGeneralManagerUsername = currentGeneralManager?.Username ?? string.Empty,
                CurrentGeneralManagerName =
                    currentGeneralManager?.DisplayName ?? administrationSettings.ManagerName,
                CurrentGeneralManagerJobTitle =
                    currentGeneralManager?.JobTitle ?? administrationSettings.ManagerTitle,
                CurrentGeneralManagerPhoneNumber =
                    currentGeneralManager?.PhoneNumber ?? administrationSettings.ManagerPhoneNumber,
                Departments = departmentList,
                DepartmentUsersByDepartmentId = departmentUsersByDepartmentId,
                UserDepartmentsByUsername = userDepartmentsByUsername,
                ManagerOptions = managerOptions,
                GeneralManagerCandidateOptions =
                    AdministrationCandidateOptionsBuilder.GetGeneralManagerCandidateOptions(
                        allUsers,
                        administrationSettings.GeneralManagerUsername
                    ),
                HandoverManagerOptions =
                    AdministrationCandidateOptionsBuilder.GetDepartmentManagerHandoverOptions(
                        selectedHandoverDepartment,
                        departments,
                        allUsers
                    ),
                PreviousManagerRoleOptions = GetDepartmentManagerFallbackRoles(),
                ManagerDepartmentId = selectedManagerDepartment?.Id ?? 0,
                ManagerUsername = selectedManagerDepartment?.ManagerUsername ?? string.Empty,
                Handover = handoverRequest,
                GeneralManagerAssignment =
                    generalManagerSeed
                    ?? new GeneralManagerAssignmentRequest
                    {
                        SelectionMode = GeneralManagerSelectionModes.ExistingUser,
                        AssignmentType = GeneralManagerAssignmentTypes.Permanent,
                        PreviousGeneralManagerAction = GeneralManagerPreviousActions.EndAssignment,
                        NewUserIsActive = true,
                    },
                TransferSourceDepartmentId = transferSourceDepartment?.Id ?? 0,
                TransferTargetDepartmentId = 0,
                TransferReplacementOptions =
                    AdministrationCandidateOptionsBuilder.GetTransferReplacementOptions(
                        transferSourceDepartment,
                        departments,
                        allUsers
                    ),
                TransferSourceUsers =
                    transferSourceDepartment == null ? new List<UserAccount>()
                    : departmentUsersByDepartmentId.TryGetValue(
                        transferSourceDepartment.Id,
                        out var sourceUsers
                    )
                        ? sourceUsers
                    : new List<UserAccount>(),
            };
        }

        private DepartmentManagementViewModel BuildInvalidTransferModel(
            DepartmentManagementViewModel model
        )
        {
            ViewData["OpenTransferPanel"] = true;
            var invalidModel = BuildDepartmentManagementViewModel(
                null,
                model.TransferSourceDepartmentId,
                model.ManagerDepartmentId > 0 ? model.ManagerDepartmentId : null
            );
            invalidModel.TransferTargetDepartmentId = model.TransferTargetDepartmentId;
            invalidModel.TransferAllUsers = model.TransferAllUsers;
            invalidModel.SelectedTransferUsernames = model.SelectedTransferUsernames;
            invalidModel.TransferReplacementManagerUsername = (
                model.TransferReplacementManagerUsername ?? string.Empty
            ).Trim();
            return invalidModel;
        }

        private DepartmentManagementViewModel BuildInvalidManagerHandoverModel(
            DepartmentManagerHandoverRequest request
        )
        {
            ViewData["OpenManagerHandoverModal"] = true;
            var invalidModel = BuildDepartmentManagementViewModel(
                null,
                null,
                null,
                request.DepartmentId > 0 ? request.DepartmentId : null
            );
            invalidModel.Handover = new DepartmentManagerHandoverRequest
            {
                DepartmentId = request.DepartmentId,
                WizardStep = request.WizardStep,
                AssignmentType = request.AssignmentType,
                CreateNewManager = request.CreateNewManager,
                ExistingManagerUsername = request.ExistingManagerUsername,
                NewManagerUsername = request.NewManagerUsername,
                NewManagerFullName = request.NewManagerFullName,
                NewManagerPhoneNumber = request.NewManagerPhoneNumber,
                ExitAction = request.ExitAction,
                PreviousManagerTargetDepartmentId = request.PreviousManagerTargetDepartmentId,
                PreviousManagerNewRole = request.PreviousManagerNewRole,
            };
            return invalidModel;
        }

        private DepartmentManagementViewModel BuildInvalidGeneralManagerModel(
            GeneralManagerAssignmentRequest request
        )
        {
            ViewData["OpenGeneralManagerWizard"] = true;
            var invalidModel = BuildDepartmentManagementViewModel();
            invalidModel.GeneralManagerAssignment = new GeneralManagerAssignmentRequest
            {
                WizardStep = request.WizardStep,
                SelectionMode = request.SelectionMode,
                ExistingUserUsername = request.ExistingUserUsername,
                NewUserUsername = request.NewUserUsername,
                NewUserFullName = request.NewUserFullName,
                NewUserPhoneNumber = request.NewUserPhoneNumber,
                NewUserPassword = request.NewUserPassword,
                NewUserConfirmPassword = request.NewUserConfirmPassword,
                NewUserJobTitle = request.NewUserJobTitle,
                NewUserDepartmentId = request.NewUserDepartmentId,
                NewUserIsActive = request.NewUserIsActive,
                PreviousGeneralManagerAction = request.PreviousGeneralManagerAction,
                PreviousGeneralManagerTargetDepartmentId =
                    request.PreviousGeneralManagerTargetDepartmentId,
                AssignmentType = request.AssignmentType,
            };
            return invalidModel;
        }

        private static string HandoverModelKey(string propertyName)
        {
            return $"{nameof(DepartmentManagementViewModel.Handover)}.{propertyName}";
        }

        private static string GeneralManagerModelKey(string propertyName)
        {
            return $"{nameof(DepartmentManagementViewModel.GeneralManagerAssignment)}.{propertyName}";
        }

        private void ClearModelStateExceptPrefix(string prefix)
        {
            var normalizedPrefix = (prefix ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
            {
                return;
            }

            var keysToRemove = ModelState
                .Keys.Where(key =>
                    !string.IsNullOrWhiteSpace(key)
                    && !string.Equals(key, normalizedPrefix, StringComparison.Ordinal)
                    && !key.StartsWith(normalizedPrefix + ".", StringComparison.Ordinal)
                )
                .ToList();

            foreach (var key in keysToRemove)
            {
                ModelState.Remove(key);
            }
        }

        private static List<string> GetDepartmentManagerFallbackRoles()
        {
            return
            [
                AppRoles.Employee,
                AppRoles.DepartmentManager,
                AppRoles.Receptionist,
                AppRoles.GateSecurity,
                AppRoles.SystemAdmin,
            ];
        }

        private BackupDashboardViewModel BuildBackupDashboardViewModel()
        {
            var backups = _backupService.GetBackups().ToList();
            var latestAutomaticBackup = backups
                .Where(item => item.FileName.Contains("-auto", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefault();

            return new BackupDashboardViewModel
            {
                BackupEnabled = _backupService.IsEnabled,
                BackupSupported = _backupService.IsSupported,
                RetentionDays = _backupService.RetentionDays,
                BackupRootPath = _backupService.BackupRootPath,
                DatabasePath = _backupService.DatabasePath,
                LastAutomaticBackupAtUtc = latestAutomaticBackup?.CreatedAtUtc,
                Backups = backups,
            };
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            var size = (double)bytes;
            var units = new[] { "KB", "MB", "GB" };
            var unitIndex = -1;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.##} {units[Math.Max(unitIndex, 0)]}";
        }
    }
}
