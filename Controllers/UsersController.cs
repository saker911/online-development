using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ManageUsers)]
    public class UsersController : Controller
    {
        private const string ToastTempDataKey = "__ToastNotifications";
        private const string CredentialNoticeTitleTempDataKey = "__CredentialNoticeTitle";
        private const string CredentialNoticeUsernameTempDataKey = "__CredentialNoticeUsername";
        private const string CredentialNoticePasswordTempDataKey =
            "__CredentialNoticeTemporaryPassword";
        private const string CredentialNoticeBadgeCodeTempDataKey = "__CredentialNoticeBadgeCode";
        private const string CredentialNoticePinTempDataKey = "__CredentialNoticeTemporaryPin";
        private const string CredentialNoticeNoteTempDataKey = "__CredentialNoticeNote";
        private static readonly JsonSerializerOptions ToastSerializerOptions = new(
            JsonSerializerDefaults.Web
        );

        private readonly IUserAdminService _userAdminService;
        private readonly IPermitService _permitService;

        public UsersController(IUserAdminService userAdminService, IPermitService permitService)
        {
            _userAdminService = userAdminService;
            _permitService = permitService;
        }

        [HttpGet]
        [Authorize(Policy = AppPolicies.ManageUsers)]
        public IActionResult WizardIdentityLookup(string username)
        {
            var normalizedUsername = (username ?? string.Empty).Trim();
            if (!IsNationalIdUsername(normalizedUsername))
            {
                return Json(
                    new
                    {
                        status = "invalid",
                        message = SaudiNationalIdOrIqamaValidator.ErrorMessage,
                    }
                );
            }

            var existingUser = GetManagedUserAccount(normalizedUsername);
            if (existingUser == null)
            {
                return Json(
                    new
                    {
                        status = "not_found",
                        message = "المستخدم غير موجود. يمكنك المتابعة للإنشاء الجديد.",
                    }
                );
            }

            var pendingPermitsCount = _permitService.GetPendingPermits(normalizedUsername).Count();
            var editUrl = Url.Action(nameof(Edit), new { id = normalizedUsername }) ?? string.Empty;
            var reactivateUrl = Url.Action(nameof(WizardReactivate)) ?? string.Empty;

            if (!existingUser.IsActive)
            {
                return Json(
                    new
                    {
                        status = "inactive",
                        message = "المستخدم موجود وموقوف.",
                        displayName = existingUser.DisplayName,
                        role = AppRoles.GetDisplayName(existingUser),
                        department = string.IsNullOrWhiteSpace(existingUser.Department)
                            ? "غير مرتبط"
                            : existingUser.Department,
                        pendingPermitsCount,
                        editUrl,
                        reactivateUrl,
                    }
                );
            }

            return Json(
                new
                {
                    status = "active",
                    message = "المستخدم موجود ونشط وسيتم تحويلك لمسار التعديل.",
                    displayName = existingUser.DisplayName,
                    role = AppRoles.GetDisplayName(existingUser),
                    department = string.IsNullOrWhiteSpace(existingUser.Department)
                        ? "غير مرتبط"
                        : existingUser.Department,
                    pendingPermitsCount,
                    editUrl,
                }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPolicies.ManageUsers)]
        public IActionResult WizardReactivate(string username)
        {
            var normalizedUsername = (username ?? string.Empty).Trim();
            var user = GetManagedUserAccount(normalizedUsername);
            if (user == null)
            {
                return Json(
                    new { status = "missing", message = "تعذر العثور على المستخدم المطلوب." }
                );
            }

            if (!CanManageUser(user))
            {
                return Json(
                    new
                    {
                        status = "denied",
                        message = "حساب مالك النظام غير متاح من هذه الشاشة إلا لمالك النظام نفسه.",
                    }
                );
            }

            var tempPassword = _userAdminService.SetUserActiveStatus(
                normalizedUsername,
                true,
                User.IsSuperAdmin()
            );
            if (string.IsNullOrWhiteSpace(tempPassword))
            {
                return Json(
                    new
                    {
                        status = "failed",
                        message = "تعذر إعادة تفعيل المستخدم. تحقق من البيانات ثم حاول مرة أخرى.",
                    }
                );
            }

            _userAdminService.RecordUserActivity(
                user.Username,
                user.DisplayName,
                "WizardReactivate",
                "إعادة تفعيل مستخدم",
                $"تمت إعادة تفعيل المستخدم {user.DisplayName} من معالج الإضافة.",
                nameof(UsersController),
                User.Identity?.Name
            );

            return Json(
                new
                {
                    status = "reactivated",
                    message = "تمت إعادة تفعيل المستخدم بنجاح.",
                    editUrl = Url.Action(nameof(Edit), new { id = normalizedUsername })
                        ?? string.Empty,
                    temporaryPassword = tempPassword,
                }
            );
        }

        public IActionResult Index()
        {
            PopulateCredentialNoticeViewData();

            var users = GetVisibleUsers().ToList();
            var tenantNamesById = User.IsSuperAdmin()
                ? _userAdminService
                    .GetTenants(includeInactive: true)
                    .ToDictionary(
                        tenant => tenant.TenantId,
                        tenant => tenant.Name,
                        StringComparer.OrdinalIgnoreCase
                    )
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var managedDepartments = _userAdminService
                .GetDepartments()
                .Where(department => !string.IsNullOrWhiteSpace(department.ManagerUsername))
                .ToList();
            var managedDepartmentNamesByUsername = managedDepartments
                .GroupBy(
                    department => department.ManagerUsername!,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(
                    group => group.Key,
                    group =>
                        group.Select(department => department.Name).FirstOrDefault()
                        ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase
                );
            var managedDepartmentIdsByUsername = managedDepartments
                .GroupBy(
                    department => department.ManagerUsername!,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(department => department.Id).FirstOrDefault(),
                    StringComparer.OrdinalIgnoreCase
                );
            var model = new UserManagementDashboardViewModel
            {
                Users = users,
                RecentActivities = CanViewAuditLogs()
                    ? FilterProtectedActivities(_userAdminService.GetRecentUserActivities(5))
                        .ToList()
                    : new List<UserActivity>(),
                RoleDistribution = users
                    .GroupBy(user => AppRoles.GetDisplayName(user))
                    .OrderBy(group =>
                        AppRoles.GetSortOrder(
                            group.FirstOrDefault()?.Role,
                            group.FirstOrDefault()?.IsSuperAdmin == true
                        )
                    )
                    .ThenBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count()),
                PermissionDistribution = users
                    .SelectMany(AppPermissions.GetGrantedPermissions)
                    .GroupBy(p => AppPermissions.GetDisplayName(p))
                    .OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ManagedDepartmentNamesByUsername = managedDepartmentNamesByUsername,
                ManagedDepartmentIdsByUsername = managedDepartmentIdsByUsername,
                TenantNamesById = tenantNamesById,
            };

            return View(model);
        }

        public IActionResult ActivityLog(
            string? query = null,
            string? actionType = null,
            string? username = null,
            int page = 1
        )
        {
            if (!CanViewAuditLogs())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            const int pageSize = 5;
            var activities = _userAdminService
                .GetUserActivities(query, actionType, username, 1000)
                .ToList();
            var allActivities = _userAdminService.GetRecentUserActivities(1000).ToList();
            activities = FilterProtectedActivities(activities).ToList();
            allActivities = FilterProtectedActivities(allActivities).ToList();
            var totalCount = activities.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            var currentPage = Math.Min(Math.Max(page, 1), totalPages);
            var pageActivities = activities
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var model = new UserActivityReportViewModel
            {
                Activities = pageActivities,
                Query = query?.Trim(),
                ActionType = actionType?.Trim(),
                Username = username?.Trim(),
                CurrentPage = currentPage,
                TotalPages = totalPages,
                TotalCount = totalCount,
                AvailableUsers = allActivities
                    .Select(x => x.Username)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList(),
                AvailableActionTypes = allActivities
                    .Select(x => x.ActionType)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList(),
            };

            return View(model);
        }

        [HttpGet]
        public IActionResult PrintActivityLog(
            string? query = null,
            string? actionType = null,
            string? username = null
        )
        {
            if (!CanViewAuditLogs())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            var activities = _userAdminService
                .GetUserActivities(query, actionType, username, 5000)
                .ToList();
            activities = FilterProtectedActivities(activities).ToList();
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
                                    .Text("تقرير سجل عمليات المستخدمين")
                                    .Bold()
                                    .FontSize(18)
                                    .AlignCenter();

                                column
                                    .Item()
                                    .Text($"إجمالي العمليات المطابقة: {activities.Count}")
                                    .SemiBold();

                                if (activities.Count == 0)
                                {
                                    column.Item().Text("لا توجد عمليات مطابقة للمعايير الحالية.");
                                }
                                else
                                {
                                    column
                                        .Item()
                                        .Table(table =>
                                        {
                                            table.ColumnsDefinition(columns =>
                                            {
                                                columns.RelativeColumn(1.2f);
                                                columns.RelativeColumn(1.5f);
                                                columns.RelativeColumn(1f);
                                                columns.RelativeColumn(2.2f);
                                                columns.RelativeColumn(1.2f);
                                            });

                                            table.Header(header =>
                                            {
                                                header
                                                    .Cell()
                                                    .Background(Colors.Blue.Darken2)
                                                    .Padding(6)
                                                    .Text("العملية")
                                                    .FontColor(Colors.White)
                                                    .SemiBold()
                                                    .AlignCenter();
                                                header
                                                    .Cell()
                                                    .Background(Colors.Blue.Darken2)
                                                    .Padding(6)
                                                    .Text("المستخدم")
                                                    .FontColor(Colors.White)
                                                    .SemiBold()
                                                    .AlignCenter();
                                                header
                                                    .Cell()
                                                    .Background(Colors.Blue.Darken2)
                                                    .Padding(6)
                                                    .Text("سياق العملية")
                                                    .FontColor(Colors.White)
                                                    .SemiBold()
                                                    .AlignCenter();
                                                header
                                                    .Cell()
                                                    .Background(Colors.Blue.Darken2)
                                                    .Padding(6)
                                                    .Text("الرسالة")
                                                    .FontColor(Colors.White)
                                                    .SemiBold()
                                                    .AlignCenter();
                                                header
                                                    .Cell()
                                                    .Background(Colors.Blue.Darken2)
                                                    .Padding(6)
                                                    .Text("الوقت")
                                                    .FontColor(Colors.White)
                                                    .SemiBold()
                                                    .AlignCenter();
                                            });

                                            foreach (var activity in activities)
                                            {
                                                table
                                                    .Cell()
                                                    .BorderBottom(1)
                                                    .BorderColor(Colors.Grey.Lighten2)
                                                    .Padding(5)
                                                    .Text(activity.ActionLabel)
                                                    .AlignRight();
                                                table
                                                    .Cell()
                                                    .BorderBottom(1)
                                                    .BorderColor(Colors.Grey.Lighten2)
                                                    .Padding(5)
                                                    .Text(
                                                        $"{activity.DisplayName}\n{activity.Username}"
                                                    )
                                                    .AlignRight();
                                                table
                                                    .Cell()
                                                    .BorderBottom(1)
                                                    .BorderColor(Colors.Grey.Lighten2)
                                                    .Padding(5)
                                                    .Text(activity.GetActorDisplay())
                                                    .AlignRight();
                                                table
                                                    .Cell()
                                                    .BorderBottom(1)
                                                    .BorderColor(Colors.Grey.Lighten2)
                                                    .Padding(5)
                                                    .Text(activity.Message)
                                                    .AlignRight();
                                                table
                                                    .Cell()
                                                    .BorderBottom(1)
                                                    .BorderColor(Colors.Grey.Lighten2)
                                                    .Padding(5)
                                                    .Text(
                                                        activity.OccurredAt.ToString(
                                                            "yyyy/MM/dd hh:mm tt",
                                                            new CultureInfo("ar-SA")
                                                        )
                                                    )
                                                    .AlignRight();
                                            }
                                        });
                                }
                            });
                    });
                })
                .GeneratePdf();

            return File(pdfBytes, "application/pdf", "UserActivityLog.pdf");
        }

        public IActionResult Create(
            string? presetRole = null,
            string? returnUrl = null,
            string? creationFlow = null,
            string? department = null
        )
        {
            var model = new UserEditViewModel
            {
                IsActive = true,
                Role = AppRoles.Receptionist,
                ApplyRoleDefaults = true,
                MustChangeOperatorPin = true,
                TenantId = GetCurrentTenantId(),
            };

            var normalizedPresetRole = (presetRole ?? string.Empty).Trim();
            var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : string.Empty;
            var isGeneralManagerFlow = string.Equals(
                normalizedPresetRole,
                AppRoles.GeneralManager,
                StringComparison.OrdinalIgnoreCase
            );
            var isDepartmentManagerFlow =
                string.Equals(
                    normalizedPresetRole,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    creationFlow,
                    "department-manager",
                    StringComparison.OrdinalIgnoreCase
                );

            if (isGeneralManagerFlow)
            {
                if (!CanAssignManagerRoles())
                {
                    QueueToastError("تعيين المديرين متاح فقط لمالك النظام أو المدير العام.");
                    return RedirectToAction(nameof(Index));
                }

                model.Role = AppRoles.GeneralManager;
                model.JobTitle = "مدير الأمن";
                ViewData["CreateFlowTitle"] = "إضافة مدير أمن";
                ViewData["CreateFlowMessage"] =
                    "مدير الأمن يراجع الطلبات المدققة ويملك الاعتماد النهائي للتصاريح.";
            }
            else if (isDepartmentManagerFlow)
            {
                if (!CanAssignManagerRoles())
                {
                    QueueToastError("تعيين المديرين متاح فقط لمالك النظام أو المدير العام.");
                    return RedirectToAction(nameof(Index));
                }

                model.Role = AppRoles.DepartmentManager;
                model.JobTitle = "مدير قسم";
                model.AutoBindManager = true;
                ViewData["CreateFlowTitle"] = "تعيين مدير قسم";
                ViewData["CreateFlowMessage"] =
                    "أنت الآن في مسار إنشاء مدير قسم. إذا كان القسم فارغًا فسيُعيّن مباشرة، وإذا كان يحتوي على مدير فسيُحوَّل المسار إلى شاشة تناقل المدراء.";
            }

            var normalizedDepartment = (department ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalizedDepartment))
            {
                var availableDepartment = _userAdminService
                    .GetDepartments()
                    .FirstOrDefault(existingDepartment =>
                        string.Equals(
                            existingDepartment.Name,
                            normalizedDepartment,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                if (availableDepartment != null)
                {
                    model.Department = availableDepartment.Name;
                }
            }

            ViewData["CreateFlowReturnUrl"] = safeReturnUrl;
            ViewData["CreateFlowKey"] = creationFlow ?? string.Empty;

            LoadRoleDefaults(model);
            PopulateTenantOptions(model);
            PopulateDepartmentOptions(model);
            PopulateGeneralManagerContext();
            return View("Create", model);
        }

        [HttpPost]
        public IActionResult Create(
            UserEditViewModel model,
            string? returnUrl = null,
            string? creationFlow = null
        )
        {
            NormalizeUserModel(model);
            ApplyTenantScope(model);
            ModelState.Clear();
            TryValidateModel(model);
            PopulateTenantOptions(model);
            ValidateTenantSelection(model);
            PopulateDepartmentOptions(model);
            PopulateGeneralManagerContext(
                _userAdminService.GetAdministrationSettings(model.TenantId, User.IsSuperAdmin())
            );
            ApplyUsernameValidation(model);
            ApplyEmailValidation(model);
            ValidateManagedUserSubmission(model);

            var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : string.Empty;
            var isGeneralManagerFlow = string.Equals(
                model.Role,
                AppRoles.GeneralManager,
                StringComparison.OrdinalIgnoreCase
            );

            if (isGeneralManagerFlow)
            {
                ViewData["CreateFlowTitle"] = "إضافة مدير أمن";
                ViewData["CreateFlowMessage"] =
                    "مدير الأمن يراجع الطلبات المدققة ويملك الاعتماد النهائي للتصاريح.";
            }

            ViewData["CreateFlowReturnUrl"] = safeReturnUrl;
            ViewData["CreateFlowKey"] = creationFlow ?? string.Empty;

            if (!ModelState.IsValid)
            {
                return View("Create", model);
            }

            if (IsManagerAssignmentRole(model.Role) && !CanAssignManagerRoles())
            {
                ModelState.AddModelError(
                    nameof(model.Role),
                    "تعيين المديرين متاح فقط لمالك النظام أو المدير العام."
                );
                return View("Create", model);
            }

            var generalManagerResult = TryHandleGeneralManagerCreation(model, safeReturnUrl);
            if (generalManagerResult != null)
            {
                return generalManagerResult;
            }

            var departmentManagerRedirect = TryRedirectDepartmentManagerCreationHandover(model);
            if (departmentManagerRedirect != null)
            {
                return departmentManagerRedirect;
            }

            var tempPassword = UserAccountService.GenerateTemporaryPassword();
            if (
                !_userAdminService.CreateUser(
                    MapToUser(model),
                    tempPassword,
                    User.IsSuperAdmin()
                )
            )
            {
                ModelState.AddModelError(string.Empty, "اسم المستخدم موجود مسبقًا.");
                return View("Create", model);
            }

            var (badgeCode, operatorPin) = ConfigureOperatorAccess(model);

            _userAdminService.RecordUserActivity(
                model.Username,
                model.FullName,
                "Create",
                "إضافة مستخدم",
                $"تمت إضافة المستخدم {model.FullName} بالدور {AppRoles.GetDisplayName(model.Role)}.",
                nameof(UsersController),
                User.Identity?.Name
            );

            var successMessage =
                $"تمت إضافة المستخدم بنجاح. كلمة مرور الدخول المؤقتة هي {tempPassword} ويجب تغييرها بعد أول دخول.{BuildOperatorSetupMessage(badgeCode, operatorPin)}";
            QueueToastSuccess(successMessage);
            SetCredentialNotice(
                title: "بيانات الدخول المؤقتة للمستخدم الجديد",
                username: model.Username,
                temporaryPassword: tempPassword,
                badgeCode: badgeCode,
                temporaryPin: operatorPin,
                note: "اعرض هذه البيانات مرة واحدة فقط للمستخدم. كلمة المرور مؤقتة لأول تشغيل، وPIN البوابة المؤقت يجب تغييره عند أول دخول تشغيلي."
            );

            if (!string.IsNullOrWhiteSpace(safeReturnUrl))
            {
                return Redirect(safeReturnUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        private IActionResult? TryRedirectDepartmentManagerCreationHandover(UserEditViewModel model)
        {
            if (
                !string.Equals(
                    model.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                ) || string.IsNullOrWhiteSpace(model.Department)
            )
            {
                return null;
            }

            var targetDepartment = _userAdminService
                .GetDepartments(model.TenantId, User.IsSuperAdmin())
                .FirstOrDefault(department =>
                    string.Equals(
                        department.Name,
                        model.Department,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            var currentManagerUsername = (targetDepartment?.ManagerUsername ?? string.Empty).Trim();
            if (targetDepartment == null || string.IsNullOrWhiteSpace(currentManagerUsername))
            {
                return null;
            }

            QueueToastWarning(
                $"القسم {targetDepartment.Name} لديه مدير قائم بالفعل. استخدم صفحة تناقل المدراء لإسناد المدير الجديد ومعالجة وضع المدير السابق أولًا."
            );

            return RedirectToAction(
                "Leadership",
                "Administration",
                new
                {
                    handoverDepartmentId = targetDepartment.Id,
                    handoverWizardStep = 2,
                    handoverCreateNewManager = true,
                    handoverNewManagerUsername = model.Username,
                    handoverNewManagerFullName = model.FullName,
                    handoverNewManagerPhoneNumber = model.PhoneNumber,
                    handoverAssignmentType = DepartmentManagerTransitionTypes.Permanent,
                    handoverExitAction = DepartmentManagerExitActions.EndAssignment,
                    handoverPreviousManagerNewRole = AppRoles.Employee,
                }
            );
        }

        private IActionResult? TryHandleGeneralManagerCreation(
            UserEditViewModel model,
            string safeReturnUrl
        )
        {
            if (!IsGeneralManagerRole(model.Role))
            {
                return null;
            }

            var administrationSettings = _userAdminService.GetAdministrationSettings(
                model.TenantId,
                User.IsSuperAdmin()
            );
            if (!IsAdministrationConfiguredForGeneralManager(administrationSettings))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "يجب تهيئة بيانات الإدارة أولًا قبل تعيين مدير عام."
                );
                PopulateGeneralManagerContext(administrationSettings);
                return View("Create", model);
            }

            var currentGeneralManagerUsername = (
                administrationSettings.GeneralManagerUsername ?? string.Empty
            ).Trim();
            if (!string.IsNullOrWhiteSpace(currentGeneralManagerUsername))
            {
                if (
                    User.IsSuperAdmin()
                    && !string.Equals(
                        model.TenantId,
                        GetCurrentTenantId(),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    ModelState.AddModelError(
                        nameof(model.Role),
                        "الجهة المختارة لديها مدير عام حالي. افتح الجهة نفسها لإدارة تناقل المدير العام."
                    );
                    PopulateGeneralManagerContext(administrationSettings);
                    return View("Create", model);
                }

                QueueToastWarning(
                    "يوجد مدير عام حالي. سيتم فتح معالج تعيين / تغيير المدير العام لاستكمال مسار التناقل ومعالجة وضع المدير السابق."
                );

                return RedirectToAction(
                    "Leadership",
                    "Administration",
                    new
                    {
                        openGeneralManagerWizard = true,
                        generalManagerWizardStep = 2,
                        generalManagerSelectionMode = GeneralManagerSelectionModes.CreateNew,
                        generalManagerNewUserUsername = model.Username,
                        generalManagerNewUserFullName = model.FullName,
                        generalManagerNewUserPhoneNumber = model.PhoneNumber,
                        generalManagerNewUserJobTitle = string.IsNullOrWhiteSpace(model.JobTitle)
                            ? "مدير عام"
                            : model.JobTitle,
                        generalManagerAssignmentType = GeneralManagerAssignmentTypes.Permanent,
                        generalManagerPreviousAction = GeneralManagerPreviousActions.EndAssignment,
                    }
                );
            }

            var temporaryPassword = UserAccountService.GenerateTemporaryPassword();
            var request = new GeneralManagerAssignmentRequest
            {
                TenantId = model.TenantId,
                SelectionMode = GeneralManagerSelectionModes.CreateNew,
                NewUserUsername = model.Username,
                NewUserFullName = model.FullName,
                NewUserPhoneNumber = model.PhoneNumber,
                NewUserEmail = model.Email,
                NewUserEmployeeNumber = model.EmployeeNumber,
                NewUserPassword = temporaryPassword,
                NewUserConfirmPassword = temporaryPassword,
                NewUserJobTitle = string.IsNullOrWhiteSpace(model.JobTitle)
                    ? "مدير عام"
                    : model.JobTitle,
                NewUserIsActive = model.IsActive,
                NewUserMustChangePassword = true,
                AssignmentType = GeneralManagerAssignmentTypes.Permanent,
                PreviousGeneralManagerAction = GeneralManagerPreviousActions.EndAssignment,
            };

            var message = _userAdminService.AssignGeneralManager(request, User.Identity?.Name);
            if (string.IsNullOrWhiteSpace(message))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "تعذر تعيين المدير العام. تحقق من بيانات الإدارة وبيانات المستخدم."
                );
                return View("Create", model);
            }

            QueueToastSuccess(
                $"{message} كلمة مرور الدخول المؤقتة هي {temporaryPassword} ويجب تغييرها بعد أول دخول."
            );
            SetCredentialNotice(
                title: "بيانات الدخول المؤقتة للمدير العام",
                username: model.Username,
                temporaryPassword: temporaryPassword,
                note: "اعرض هذه البيانات مرة واحدة فقط للمدير العام الجديد. كلمة المرور مؤقتة لأول تشغيل ثم تُستبدل مباشرة."
            );

            if (!string.IsNullOrWhiteSpace(safeReturnUrl))
            {
                return Redirect(safeReturnUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        public IActionResult Edit(string id)
        {
            PopulateCredentialNoticeViewData();

            if (string.Equals(id, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Profile", "Account");
            }

            var user = GetManagedUserAccount(id);
            if (user == null)
            {
                return NotFound();
            }

            if (!CanManageUser(user))
            {
                return RedirectProtectedSuperAdminAccess();
            }

            var model = MapToViewModel(user);
            if (user.CanScanOperations && string.IsNullOrWhiteSpace(model.OperatorBadgeCode))
            {
                model.OperatorBadgeCode = _userAdminService.EnsureOperatorBadgeCode(
                    user.Username,
                    ignoreTenantFilters: User.IsSuperAdmin()
                );
            }
            PopulateTenantOptions(model);
            PopulateDepartmentOptions(model);
            PopulateGeneralManagerContext();
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(UserEditViewModel model)
        {
            NormalizeUserModel(model);
            ApplyTenantScope(model);
            var originalUsername = string.IsNullOrWhiteSpace(model.OriginalUsername)
                ? model.Username
                : model.OriginalUsername;
            var existingUser = GetManagedUserAccount(originalUsername);
            if (existingUser != null && !CanManageUser(existingUser))
            {
                return RedirectProtectedSuperAdminAccess();
            }
            ModelState.Clear();
            TryValidateModel(model);
            PopulateTenantOptions(model);
            ValidateTenantSelection(model);
            PopulateDepartmentOptions(model);
            PopulateGeneralManagerContext();
            ApplyUsernameValidation(model);
            ApplyEmailValidation(model);
            ValidateManagedUserSubmission(model);

            var isEditingCurrentUser = string.Equals(
                originalUsername,
                User.Identity?.Name,
                StringComparison.OrdinalIgnoreCase
            );

            if (isEditingCurrentUser)
            {
                return RedirectToAction("Profile", "Account");
            }

            if (isEditingCurrentUser && existingUser != null)
            {
                RestoreSelfEditAdministrativeFields(model, existingUser);
            }

            if (
                string.Equals(
                    originalUsername,
                    User.Identity?.Name,
                    StringComparison.OrdinalIgnoreCase
                ) && !model.IsActive
            )
            {
                ModelState.AddModelError(
                    nameof(model.IsActive),
                    "لا يمكن إيقاف الحساب الحالي من داخل شاشة التعديل."
                );
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Auto-bind manager username from department if requested
            if (
                model.AutoBindManager
                && string.IsNullOrWhiteSpace(model.ManagerUsername)
                && !string.IsNullOrWhiteSpace(model.Department)
            )
            {
                var dept = _userAdminService
                    .GetDepartments(model.TenantId, User.IsSuperAdmin())
                    .FirstOrDefault(d =>
                        string.Equals(d.Name, model.Department, StringComparison.OrdinalIgnoreCase)
                    );
                if (dept != null && !string.IsNullOrWhiteSpace(dept.ManagerUsername))
                {
                    model.ManagerUsername = dept.ManagerUsername;
                }
            }

            var updateResult = _userAdminService.UpdateUser(
                MapToUser(model),
                model.NewPassword,
                originalUsername,
                User.IsSuperAdmin()
            );
            if (!updateResult)
            {
                ModelState.AddModelError(
                    string.Empty,
                    model.IsSuperAdmin
                        ? "تعذر تحديث حساب مالك النظام. تأكد من أن اسم المستخدم الجديد غير مستخدم حاليًا."
                        : "تعذر تعديل المستخدم: المستخدم مرتبط كمدير لقسم - قم بإعادة تعيين مدير القسم أولًا."
                );
                return View(model);
            }
            var (badgeCode, operatorPin) = ConfigureOperatorAccess(model);

            _userAdminService.RecordUserActivity(
                originalUsername,
                model.FullName,
                "Update",
                "تحديث مستخدم",
                $"تم تحديث بيانات المستخدم {model.FullName} والصلاحيات المرتبطة به.",
                nameof(UsersController),
                User.Identity?.Name
            );

            if (isEditingCurrentUser)
            {
                var sessionId = User
                    .Claims.FirstOrDefault(claim => claim.Type == "sessionId")
                    ?.Value;
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    _userAdminService.RemoveSession(sessionId);
                }

                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                TempData["SubmittedUsername"] = model.Username;
                QueueToastSuccess(
                    string.IsNullOrWhiteSpace(model.NewPassword)
                        ? "تم تحديث حسابك بنجاح. سجّل الدخول مرة أخرى لتطبيق الصلاحيات والبيانات الجديدة."
                        : "تم تحديث حسابك وكلمة المرور بنجاح. سجّل الدخول مرة أخرى باستخدام كلمة المرور الجديدة."
                );
                return RedirectToAction("Login", "Account");
            }

            QueueToastSuccess(
                $"تم تحديث المستخدم والصلاحيات بنجاح.{BuildOperatorSetupMessage(badgeCode, operatorPin)}"
            );
            return RedirectToAction(nameof(Index));
        }

        public IActionResult OperatorBadge(string id)
        {
            var user = GetManagedUserAccount(id);
            if (user == null || string.IsNullOrWhiteSpace(user.OperatorBadgeCode))
            {
                return NotFound();
            }

            if (!CanManageUser(user))
            {
                return RedirectProtectedSuperAdminAccess();
            }

            var bytes = UserBadgeImageRenderer.RenderCode128(user.OperatorBadgeCode);
            return File(bytes, "image/png");
        }

        public IActionResult PrintOperatorBadge(string id)
        {
            var user = GetManagedUserAccount(id);
            if (user == null || string.IsNullOrWhiteSpace(user.OperatorBadgeCode))
            {
                return NotFound();
            }

            if (!CanManageUser(user))
            {
                return RedirectProtectedSuperAdminAccess();
            }

            return View(user);
        }

        public IActionResult UserBadge(string id)
        {
            var user = GetManagedUserAccount(id);
            if (user == null)
            {
                return NotFound();
            }

            if (!CanManageUser(user))
            {
                return RedirectProtectedSuperAdminAccess();
            }

            var bytes = UserBadgeImageRenderer.RenderCode128(user.Username);
            return File(bytes, "image/png");
        }

        public IActionResult PrintUserBadge(string id)
        {
            var user = GetManagedUserAccount(id);
            if (user == null)
            {
                return NotFound();
            }

            if (!CanManageUser(user))
            {
                return RedirectProtectedSuperAdminAccess();
            }

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetTemporaryPassword(string id)
        {
            var user = GetManagedUserAccount(id);
            if (user == null)
            {
                QueueToastError("تعذر العثور على المستخدم المطلوب.");
                return RedirectToAction(nameof(Index));
            }

            if (!CanManageUser(user))
            {
                return RedirectProtectedSuperAdminAccess();
            }

            if (user.IsSuperAdmin)
            {
                QueueToastError(
                    "لا يمكن إعادة تعيين كلمة مرور مالك النظام من هذه الشاشة. استخدم الملف الشخصي للحساب نفسه."
                );
                return RedirectToAction(nameof(Edit), new { id });
            }

            var temporaryPassword = UserAccountService.GenerateTemporaryPassword();
            var updated = _userAdminService.UpdateUser(
                user,
                temporaryPassword,
                user.Username,
                User.IsSuperAdmin()
            );
            if (!updated)
            {
                QueueToastError("تعذر إعادة تعيين كلمة المرور المؤقتة لهذا المستخدم.");
                return RedirectToAction(nameof(Edit), new { id });
            }

            _userAdminService.RecordUserActivity(
                user.Username,
                user.DisplayName,
                "ResetTemporaryPassword",
                "إعادة تعيين كلمة المرور المؤقتة",
                $"تمت إعادة تعيين كلمة مرور مؤقتة عشوائية للمستخدم {user.DisplayName} مع إجباره على التغيير عند أول دخول.",
                nameof(UsersController),
                User.Identity?.Name
            );

            QueueToastSuccess(
                $"تمت إعادة تعيين كلمة المرور المؤقتة بنجاح. كلمة المرور الحالية هي {temporaryPassword} ويجب تغييرها عند أول دخول."
            );
            SetCredentialNotice(
                title: "بيانات الدخول المؤقتة بعد إعادة التعيين",
                username: user.Username,
                temporaryPassword: temporaryPassword,
                note: "أرسل كلمة المرور الجديدة للمستخدم مرة واحدة فقط. لن يستمر استخدام هذه الكلمة بعد أول دخول ناجح وتغييرها."
            );
            return RedirectToAction(nameof(Edit), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetTemporaryOperatorPin(string id)
        {
            var user = GetManagedUserAccount(id);
            if (user == null)
            {
                QueueToastError("تعذر العثور على المستخدم المطلوب.");
                return RedirectToAction(nameof(Index));
            }

            if (!CanManageUser(user))
            {
                return RedirectProtectedSuperAdminAccess();
            }

            if (!user.CanScanOperations)
            {
                QueueToastError("هذا المستخدم غير مخول بإعدادات تشغيل البوابة.");
                return RedirectToAction(nameof(Edit), new { id });
            }

            var badgeCode = _userAdminService.EnsureOperatorBadgeCode(
                user.Username,
                user.OperatorBadgeCode,
                User.IsSuperAdmin()
            );
            var generatedTemporaryPin = UserAccountService.GenerateTemporaryOperatorPin();
            var temporaryPin = _userAdminService.ConfigureOperatorCredentials(
                user.Username,
                badgeCode,
                generatedTemporaryPin,
                true,
                User.IsSuperAdmin()
            );

            if (string.IsNullOrWhiteSpace(temporaryPin))
            {
                QueueToastError("تعذر إعادة تعيين PIN المؤقت لمشغل البوابة.");
                return RedirectToAction(nameof(Edit), new { id });
            }

            _userAdminService.RecordUserActivity(
                user.Username,
                user.DisplayName,
                "ResetTemporaryOperatorPin",
                "إعادة تعيين PIN المؤقت",
                $"تمت إعادة تعيين PIN مؤقت لمشغل البوابة {user.DisplayName} مع إجباره على التغيير عند أول دخول. باركود المشغل: {badgeCode}.",
                nameof(UsersController),
                User.Identity?.Name
            );

            QueueToastSuccess(
                $"تمت إعادة تعيين PIN المؤقت بنجاح. PIN الحالي هو {temporaryPin} وباركود المشغل هو {badgeCode}."
            );
            SetCredentialNotice(
                title: "بيانات تشغيل البوابة المؤقتة",
                username: user.Username,
                badgeCode: badgeCode,
                temporaryPin: temporaryPin,
                note: "هذا الـ PIN مؤقت لمرة التشغيل الأولى فقط، ويجب تغييره عند أول دخول تشغيلي للمشغل."
            );
            return RedirectToAction(nameof(Edit), new { id });
        }

        [HttpPost]
        public IActionResult Activate(string id)
        {
            return SetUserActiveState(id, true);
        }

        [HttpPost]
        public IActionResult Deactivate(string id)
        {
            if (string.Equals(id, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            {
                QueueToastError("لا يمكن إيقاف الحساب الحالي من نفس الجلسة.");
                return RedirectToAction(nameof(Index));
            }

            var targetUser = GetManagedUserAccount(id);
            if (targetUser?.IsSuperAdmin == true)
            {
                QueueToastError("لا يمكن إيقاف حساب مالك النظام.");
                return RedirectToAction(nameof(Index));
            }

            var managedDepartment = _userAdminService
                .GetDepartments()
                .FirstOrDefault(department =>
                    string.Equals(
                        department.ManagerUsername,
                        id,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            if (managedDepartment != null)
            {
                var managedDepartmentName = managedDepartment.Name;
                var managedUserName = GetManagedUserAccount(id)?.DisplayName ?? id;
                QueueToastWarning(
                    $"لا يمكن إيقاف {managedUserName} لأنه المدير الحالي لقسم {managedDepartmentName}. عيّن بديلًا أولًا ثم أعد محاولة الإيقاف."
                );
                return RedirectToAction(
                    "Leadership",
                    "Administration",
                    new { managerDepartmentId = managedDepartment.Id }
                );
            }

            return SetUserActiveState(id, false);
        }

        [HttpPost]
        public IActionResult ApplyRoleDefaults(
            UserEditViewModel model,
            string? returnUrl = null,
            string? creationFlow = null
        )
        {
            NormalizeUserModel(model);
            ApplyTenantScope(model);
            var originalUsername = string.IsNullOrWhiteSpace(model.OriginalUsername)
                ? model.Username
                : model.OriginalUsername;
            var existingUser = GetManagedUserAccount(originalUsername);
            var isCreateFlow =
                string.IsNullOrWhiteSpace(model.OriginalUsername) && existingUser == null;
            if (existingUser != null && !CanManageUser(existingUser))
            {
                return RedirectProtectedSuperAdminAccess();
            }

            ValidateManagedUserSubmission(model);
            PopulateTenantOptions(model);
            ValidateTenantSelection(model);

            if (!ModelState.IsValid)
            {
                PopulateDepartmentOptions(model);
                PopulateGeneralManagerContext();
                if (isCreateFlow)
                {
                    PopulateCreateFlowViewData(model, returnUrl, creationFlow);
                    return View("Create", model);
                }

                return View("Edit", model);
            }

            var isEditingCurrentUser =
                existingUser != null
                && string.Equals(
                    originalUsername,
                    User.Identity?.Name,
                    StringComparison.OrdinalIgnoreCase
                );

            if (isEditingCurrentUser)
            {
                return RedirectToAction("Profile", "Account");
            }

            if (isEditingCurrentUser && existingUser != null)
            {
                RestoreSelfEditAdministrativeFields(model, existingUser);
            }
            else
            {
                LoadRoleDefaults(model);
            }

            PopulateTenantOptions(model);
            PopulateDepartmentOptions(model);
            PopulateGeneralManagerContext();
            if (isCreateFlow)
            {
                PopulateCreateFlowViewData(model, returnUrl, creationFlow);
                return View("Create", model);
            }

            return View("Edit", model);
        }

        private void PopulateCreateFlowViewData(
            UserEditViewModel model,
            string? returnUrl,
            string? creationFlow
        )
        {
            var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : string.Empty;
            var normalizedCreationFlow = creationFlow ?? string.Empty;
            var isGeneralManagerFlow =
                string.Equals(
                    normalizedCreationFlow,
                    "general-manager-transfer",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    model.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                );
            var isDepartmentManagerFlow =
                string.Equals(
                    normalizedCreationFlow,
                    "department-manager",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    model.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                );

            if (isGeneralManagerFlow)
            {
                ViewData["CreateFlowTitle"] = "تعيين أو تغيير المدير العام";
                ViewData["CreateFlowMessage"] =
                    "أنت الآن في مسار إنشاء مدير عام جديد. بعد الحفظ ستتحدث بيانات الإدارة تلقائيًا من الحساب النشط بدون أي ربط يدوي إضافي.";
            }
            else if (isDepartmentManagerFlow)
            {
                ViewData["CreateFlowTitle"] = "تعيين مدير قسم";
                ViewData["CreateFlowMessage"] =
                    "أنت الآن في مسار إنشاء مدير قسم. إذا كان القسم فارغًا فسيُعيّن مباشرة، وإذا كان يحتوي على مدير فسيُحوَّل المسار إلى شاشة تناقل المدراء.";
            }

            ViewData["CreateFlowReturnUrl"] = safeReturnUrl;
            ViewData["CreateFlowKey"] = normalizedCreationFlow;
        }

        private static void RestoreSelfEditAdministrativeFields(
            UserEditViewModel model,
            UserAccount existingUser
        )
        {
            var canManageOwnGateSettings = existingUser.CanScanOperations;

            model.Username = existingUser.Username;
            model.FullName = string.IsNullOrWhiteSpace(existingUser.FullName)
                ? existingUser.DisplayName
                : existingUser.FullName;
            model.PhoneNumber = existingUser.PhoneNumber;
            model.JobTitle = existingUser.JobTitle;
            model.IsActive = existingUser.IsActive;
            model.IsSuperAdmin = existingUser.IsSuperAdmin;
            model.Role = existingUser.Role;
            model.Department = existingUser.Department;
            model.ManagerUsername = existingUser.ManagerUsername;
            model.AutoBindManager = false;
            model.ApplyRoleDefaults = false;
            model.OperatorBadgeCode = existingUser.OperatorBadgeCode;
            if (!canManageOwnGateSettings)
            {
                model.TemporaryOperatorPin = string.Empty;
                model.MustChangeOperatorPin = existingUser.MustChangeOperatorPin;
            }
            CopyPermissions(existingUser, model);
        }

        private static void CopyPermissions(UserAccount source, UserEditViewModel target)
        {
            target.CanViewDashboard = source.CanViewDashboard;
            target.CanViewPermits = source.CanViewPermits;
            target.CanViewVisitorPermits = source.CanViewVisitorPermits;
            target.CanCreatePermit = source.CanCreatePermit;
            target.CanCreateVisitorPermit = source.CanCreateVisitorPermit;
            target.CanEditPermit = source.CanEditPermit;
            target.CanEditVisitorPermit = source.CanEditVisitorPermit;
            target.CanApprovePermit = source.CanApprovePermit;
            target.CanApproveLeaveRequest = source.CanApproveLeaveRequest;
            target.CanStopPermit = source.CanStopPermit;
            target.CanReviewUnauthorizedExit = source.CanReviewUnauthorizedExit;
            target.CanViewVisits = source.CanViewVisits;
            target.CanCreateVisit = source.CanCreateVisit;
            target.CanEditVisit = source.CanEditVisit;
            target.CanApproveDetainedVisit = source.CanApproveDetainedVisit;
            target.CanApproveVisits = source.CanApproveVisits;
            target.CanScanOperations = source.CanScanOperations;
            target.CanViewDisplays = source.CanViewDisplays;
            target.CanManageUsers = source.CanManageUsers;
            target.CanManageDepartments = source.CanManageDepartments;
            target.CanManageAdministration = source.CanManageAdministration;
            target.CanManageDelegations = source.CanManageDelegations;
        }

        private static void LoadRoleDefaults(UserEditViewModel model)
        {
            var roleUser = new UserAccount { Role = model.Role, IsSuperAdmin = model.IsSuperAdmin };
            AppPermissions.ApplyRoleDefaults(roleUser);

            model.CanViewDashboard = roleUser.CanViewDashboard;
            model.CanViewPermits = roleUser.CanViewPermits;
            model.CanViewVisitorPermits = roleUser.CanViewVisitorPermits;
            model.CanCreatePermit = roleUser.CanCreatePermit;
            model.CanCreateVisitorPermit = roleUser.CanCreateVisitorPermit;
            model.CanEditPermit = roleUser.CanEditPermit;
            model.CanEditVisitorPermit = roleUser.CanEditVisitorPermit;
            model.CanApprovePermit = roleUser.CanApprovePermit;
            model.CanApproveLeaveRequest = roleUser.CanApproveLeaveRequest;
            model.CanStopPermit = roleUser.CanStopPermit;
            model.CanReviewUnauthorizedExit = roleUser.CanReviewUnauthorizedExit;
            model.CanViewVisits = roleUser.CanViewVisits;
            model.CanCreateVisit = roleUser.CanCreateVisit;
            model.CanEditVisit = roleUser.CanEditVisit;
            model.CanApproveDetainedVisit = roleUser.CanApproveDetainedVisit;
            model.CanApproveVisits = roleUser.CanApproveVisits;
            model.CanScanOperations = roleUser.CanScanOperations;
            model.CanViewDisplays = roleUser.CanViewDisplays;
            model.CanManageUsers = roleUser.CanManageUsers;
            model.CanManageDepartments = roleUser.CanManageDepartments;
            model.CanManageAdministration = roleUser.CanManageAdministration;
            model.CanManageDelegations = roleUser.CanManageDelegations;
        }

        private static UserEditViewModel MapToViewModel(UserAccount user)
        {
            return new UserEditViewModel
            {
                Username = user.Username,
                OriginalUsername = user.Username,
                TenantId = string.IsNullOrWhiteSpace(user.TenantId)
                    ? TenantDefaults.DefaultTenantId
                    : user.TenantId,
                FullName = string.IsNullOrWhiteSpace(user.FullName)
                    ? user.DisplayName
                    : user.FullName,
                Email = NormalizeEmailForDisplay(user),
                Department =
                    user.IsSuperAdmin || IsGeneralManagerRole(user.Role)
                        ? string.Empty
                        : user.Department,
                EmployeeNumber = user.EmployeeNumber,
                JobTitle = user.JobTitle,
                OperatorBadgeCode = user.OperatorBadgeCode,
                PhoneNumber = user.PhoneNumber,
                IsActive = user.IsActive,
                IsSuperAdmin = user.IsSuperAdmin,
                Role = user.Role,
                ApplyRoleDefaults = false,
                MustChangeOperatorPin = user.MustChangeOperatorPin,
                CanViewDashboard = user.CanViewDashboard,
                CanViewPermits = user.CanViewPermits,
                CanViewVisitorPermits = user.CanViewVisitorPermits,
                CanCreatePermit = user.CanCreatePermit,
                CanCreateVisitorPermit = user.CanCreateVisitorPermit,
                CanEditPermit = user.CanEditPermit,
                CanEditVisitorPermit = user.CanEditVisitorPermit,
                CanApprovePermit = user.CanApprovePermit,
                CanApproveLeaveRequest = user.CanApproveLeaveRequest,
                CanStopPermit = user.CanStopPermit,
                CanReviewUnauthorizedExit = user.CanReviewUnauthorizedExit,
                CanViewVisits = user.CanViewVisits,
                CanCreateVisit = user.CanCreateVisit,
                CanEditVisit = user.CanEditVisit,
                CanApproveDetainedVisit = user.CanApproveDetainedVisit,
                CanApproveVisits = user.CanApproveVisits,
                CanScanOperations = user.CanScanOperations,
                CanViewDisplays = user.CanViewDisplays,
                CanManageUsers = user.CanManageUsers,
                CanManageDepartments = user.CanManageDepartments,
                CanManageAdministration = user.CanManageAdministration,
                CanManageDelegations = user.CanManageDelegations,
                ManagerUsername =
                    user.IsSuperAdmin || ShouldHideManagerBinding(user.Role)
                        ? string.Empty
                        : user.ManagerUsername,
            };
        }

        private static UserAccount MapToUser(UserEditViewModel model)
        {
            var fullName = (model.FullName ?? string.Empty).Trim();
            var normalizedRole = (model.Role ?? string.Empty).Trim();
            var user = new UserAccount
            {
                Username = (model.Username ?? string.Empty).Trim(),
                TenantId = string.IsNullOrWhiteSpace(model.TenantId)
                    ? TenantDefaults.DefaultTenantId
                    : model.TenantId.Trim(),
                DisplayName = fullName,
                FullName = fullName,
                Department =
                    model.IsSuperAdmin || IsGeneralManagerRole(normalizedRole)
                        ? string.Empty
                        : (model.Department ?? string.Empty).Trim(),
                EmployeeNumber = (model.EmployeeNumber ?? string.Empty).Trim(),
                JobTitle = (model.JobTitle ?? string.Empty).Trim(),
                OperatorBadgeCode = (model.OperatorBadgeCode ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant(),
                MustChangeOperatorPin = model.MustChangeOperatorPin,
                PhoneNumber = (model.PhoneNumber ?? string.Empty).Trim(),
                Email = NormalizeSubmittedEmail(model.Email),
                IsActive = model.IsActive,
                IsSuperAdmin = model.IsSuperAdmin,
                Role = normalizedRole,
                CanViewDashboard = model.CanViewDashboard,
                CanViewPermits = model.CanViewPermits,
                CanViewVisitorPermits = model.CanViewVisitorPermits,
                CanCreatePermit = model.CanCreatePermit,
                CanCreateVisitorPermit = model.CanCreateVisitorPermit,
                CanEditPermit = model.CanEditPermit,
                CanEditVisitorPermit = model.CanEditVisitorPermit,
                CanApprovePermit = model.CanApprovePermit,
                CanApproveLeaveRequest = model.CanApproveLeaveRequest,
                CanStopPermit = model.CanStopPermit,
                CanReviewUnauthorizedExit = model.CanReviewUnauthorizedExit,
                CanViewVisits = model.CanViewVisits,
                CanCreateVisit = model.CanCreateVisit,
                CanEditVisit = model.CanEditVisit,
                CanApproveDetainedVisit = model.CanApproveDetainedVisit,
                CanApproveVisits = model.CanApproveVisits,
                CanScanOperations = model.CanScanOperations,
                CanViewDisplays = model.CanViewDisplays,
                CanManageUsers = model.CanManageUsers,
                CanManageDepartments = model.CanManageDepartments,
                CanManageAdministration = model.CanManageAdministration,
                CanManageDelegations = model.CanManageDelegations,
                ManagerUsername =
                    model.IsSuperAdmin || ShouldHideManagerBinding(normalizedRole)
                        ? string.Empty
                        : (model.ManagerUsername ?? string.Empty).Trim(),
            };

            if (model.ApplyRoleDefaults)
            {
                AppPermissions.ApplyRoleDefaults(user);
            }

            return user;
        }

        private static void NormalizeUserModel(UserEditViewModel model)
        {
            model.OriginalUsername = (model.OriginalUsername ?? string.Empty).Trim();
            model.Username = string.IsNullOrWhiteSpace(model.OriginalUsername)
                ? new string((model.Username ?? string.Empty).Where(char.IsDigit).ToArray())
                : (model.Username ?? string.Empty).Trim();
            model.FullName = (model.FullName ?? string.Empty).Trim();
            model.Email = (model.Email ?? string.Empty).Trim();
            model.EmployeeNumber = (model.EmployeeNumber ?? string.Empty).Trim();
            model.Department = (model.Department ?? string.Empty).Trim();
            model.TenantId = (model.TenantId ?? string.Empty).Trim();
            model.JobTitle = (model.JobTitle ?? string.Empty).Trim();
            model.PhoneNumber = (model.PhoneNumber ?? string.Empty).Trim();
            model.ManagerUsername = (model.ManagerUsername ?? string.Empty).Trim();
            model.NewPassword = model.NewPassword ?? string.Empty;
            model.OperatorBadgeCode = (model.OperatorBadgeCode ?? string.Empty)
                .Trim()
                .ToUpperInvariant();
            model.TemporaryOperatorPin = string.Empty;
            model.MustChangeOperatorPin = false;
            model.Role = (model.Role ?? string.Empty).Trim();

            if (IsGeneralManagerRole(model.Role))
            {
                model.Department = string.Empty;
            }

            if (model.IsSuperAdmin)
            {
                model.Department = string.Empty;
                model.Role = AppRoles.SystemAdmin;
                model.IsActive = true;
            }

            if (ShouldHideManagerBinding(model.Role) || model.IsSuperAdmin)
            {
                model.ManagerUsername = string.Empty;
                model.AutoBindManager = false;
            }
        }

        private static bool IsGeneralManagerRole(string? role)
        {
            return string.Equals(role, AppRoles.GeneralManager, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManagerAssignmentRole(string? role)
        {
            return IsGeneralManagerRole(role)
                || string.Equals(
                    role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool ShouldHideManagerBinding(string? role)
        {
            return true;
        }

        private static bool IsAdministrationConfiguredForGeneralManager(
            AdministrationSettings settings
        )
        {
            return !string.IsNullOrWhiteSpace(settings.OrganizationName)
                && !string.IsNullOrWhiteSpace(settings.DepartmentName);
        }

        private void PopulateGeneralManagerContext(AdministrationSettings? settings = null)
        {
            var administrationSettings = settings ?? _userAdminService.GetAdministrationSettings();
            var isConfigured = IsAdministrationConfiguredForGeneralManager(administrationSettings);
            var currentGeneralManagerUsername = (
                administrationSettings.GeneralManagerUsername ?? string.Empty
            ).Trim();
            var currentGeneralManager = string.IsNullOrWhiteSpace(currentGeneralManagerUsername)
                ? null
                : GetManagedUserAccount(currentGeneralManagerUsername);

            ViewData["GeneralManagerAdministrationName"] = string.IsNullOrWhiteSpace(
                administrationSettings.DepartmentName
            )
                ? administrationSettings.OrganizationName
                : administrationSettings.DepartmentName;
            ViewData["GeneralManagerAdministrationOrganization"] =
                administrationSettings.OrganizationName;
            ViewData["CanAssignGeneralManager"] = isConfigured;
            ViewData["CurrentGeneralManagerName"] =
                currentGeneralManager?.DisplayName ?? administrationSettings.ManagerName;
            ViewData["CurrentGeneralManagerUsername"] = currentGeneralManagerUsername;
            ViewData["CurrentGeneralManagerActionSummary"] = string.IsNullOrWhiteSpace(
                currentGeneralManagerUsername
            )
                ? "لا يوجد مدير عام حالي. سيتم ربط المدير الجديد بالإدارة مباشرة."
                : "سيتم تحويلك إلى معالج التناقل الحالي لتحديد إجراء المدير العام السابق قبل الاعتماد النهائي.";
        }

        private void ApplyEmailValidation(UserEditViewModel model)
        {
            var email = NormalizeSubmittedEmail(model.Email);
            model.Email = email;
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            if (!new EmailAddressAttribute().IsValid(email))
            {
                ModelState.AddModelError(nameof(model.Email), "صيغة البريد الإلكتروني غير صحيحة.");
            }
        }

        private static string NormalizeSubmittedEmail(string? email)
        {
            return (email ?? string.Empty).Trim();
        }

        private static string NormalizeEmailForDisplay(UserAccount user)
        {
            var email = NormalizeSubmittedEmail(user.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                return string.Empty;
            }

            if (
                email.All(char.IsDigit)
                || string.Equals(email, user.Username, StringComparison.OrdinalIgnoreCase)
                || string.Equals(email, user.PhoneNumber, StringComparison.OrdinalIgnoreCase)
                || string.Equals(email, user.OperatorBadgeCode, StringComparison.OrdinalIgnoreCase)
                || !new EmailAddressAttribute().IsValid(email)
            )
            {
                return string.Empty;
            }

            return email;
        }

        private (string badgeCode, string? temporaryPin) ConfigureOperatorAccess(
            UserEditViewModel model
        )
        {
            var canConfigureOperator = model.CanScanOperations;
            if (!canConfigureOperator && model.ApplyRoleDefaults)
            {
                var roleUser = new UserAccount
                {
                    Role = model.Role ?? string.Empty,
                    IsSuperAdmin = model.IsSuperAdmin,
                };
                AppPermissions.ApplyRoleDefaults(roleUser);
                canConfigureOperator = roleUser.CanScanOperations;
            }

            if (!canConfigureOperator)
            {
                return (string.Empty, null);
            }

            var badgeCode = _userAdminService.EnsureOperatorBadgeCode(
                model.Username,
                model.OperatorBadgeCode,
                User.IsSuperAdmin()
            );
            _userAdminService.ConfigureOperatorCredentials(
                model.Username,
                badgeCode,
                null,
                false,
                User.IsSuperAdmin()
            );

            return (badgeCode, null);
        }

        private void ApplyUsernameValidation(UserEditViewModel model)
        {
            var normalizedUsername = (model.Username ?? string.Empty).Trim();
            var normalizedOriginalUsername = (model.OriginalUsername ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalizedOriginalUsername))
            {
                if (!IsNationalIdUsername(normalizedUsername))
                {
                    ModelState.AddModelError(
                        nameof(model.Username),
                        SaudiNationalIdOrIqamaValidator.ErrorMessage
                    );
                }

                return;
            }

            if (model.IsSuperAdmin)
            {
                if (!IsNationalIdUsername(normalizedUsername))
                {
                    ModelState.AddModelError(
                        nameof(model.Username),
                        SaudiNationalIdOrIqamaValidator.ErrorMessage
                    );
                }

                return;
            }

            if (
                !string.Equals(
                    normalizedUsername,
                    normalizedOriginalUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ModelState.AddModelError(
                    nameof(model.Username),
                    "لا يمكن تعديل اسم المستخدم بعد إنشاء الحساب."
                );
            }
        }

        private static string BuildOperatorSetupMessage(string badgeCode, string? temporaryPin)
        {
            if (string.IsNullOrWhiteSpace(badgeCode))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(temporaryPin))
            {
                return $" رمز مشغل شاشة العرض/البوابة: {badgeCode}.";
            }

            return $" رمز مشغل شاشة العرض/البوابة: {badgeCode}. PIN المؤقت لشاشة العرض/البوابة: {temporaryPin}.";
        }

        private void PopulateDepartmentOptions(UserEditViewModel model)
        {
            var departments = _userAdminService
                .GetDepartments(model.TenantId, User.IsSuperAdmin())
                .Where(department => department.IsActive)
                .Where(department => !string.IsNullOrWhiteSpace(department.Name))
                .GroupBy(department => department.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(department => department.Name)
                .ToList();

            model.DepartmentOptions = departments.Select(department => department.Name).ToList();
            model.DepartmentManagerSummaryByName = departments.ToDictionary(
                department => department.Name,
                department =>
                    string.IsNullOrWhiteSpace(department.ManagerUsername)
                        ? ""
                        : $"المدير الحالي: {(string.IsNullOrWhiteSpace(department.ManagerDisplayName) ? department.ManagerUsername : department.ManagerDisplayName)} ({department.ManagerUsername})",
                StringComparer.OrdinalIgnoreCase
            );
        }

        private IActionResult SetUserActiveState(string id, bool isActive)
        {
            var user = GetManagedUserAccount(id);
            if (user != null && !CanManageUser(user))
            {
                return RedirectProtectedSuperAdminAccess();
            }

            if (user?.IsSuperAdmin == true && !isActive)
            {
                QueueToastError("لا يمكن إيقاف حساب مالك النظام.");
                return RedirectToAction(nameof(Index));
            }

            var result =
                user != null
                    ? _userAdminService.SetUserActiveStatus(id, isActive, User.IsSuperAdmin())
                    : null;
            if (result != null)
            {
                _userAdminService.RecordUserActivity(
                    user!.Username,
                    user.DisplayName,
                    isActive ? "Activate" : "Deactivate",
                    isActive ? "تفعيل مستخدم" : "إيقاف مستخدم",
                    isActive
                        ? $"تم تفعيل المستخدم {user.DisplayName}."
                        : $"تم إيقاف المستخدم {user.DisplayName}.",
                    nameof(UsersController),
                    User.Identity?.Name
                );

                QueueToastSuccess(
                    isActive
                        ? $"تم تفعيل المستخدم بنجاح. كلمة مرور الدخول المؤقتة هي {result} ويجب تغييرها بعد أول دخول."
                        : "تم إيقاف المستخدم بنجاح."
                );

                if (isActive)
                {
                    SetCredentialNotice(
                        title: "بيانات الدخول المؤقتة بعد إعادة التفعيل",
                        username: user.Username,
                        temporaryPassword: result,
                        note: "هذه الكلمة صالحة لأول دخول فقط بعد إعادة التفعيل، ويجب على المستخدم تغييرها مباشرة."
                    );
                }
            }
            else
            {
                QueueToastError(
                    user == null
                        ? "تعذر العثور على المستخدم المطلوب."
                        : "تعذر تنفيذ العملية المطلوبة لهذا الحساب."
                );
            }

            return RedirectToAction(nameof(Index));
        }

        private bool CanViewAuditLogs()
        {
            return User.IsSuperAdmin()
                || User.IsInRole(AppRoles.GeneralManager)
                || User.IsInRole(AppRoles.DepartmentManager)
                || User.IsInRole(AppRoles.SystemAdmin);
        }

        private bool CanAssignManagerRoles()
        {
            return User.IsSuperAdmin() || User.IsInRole(AppRoles.GeneralManager);
        }

        private void ValidateManagedUserSubmission(UserEditViewModel model)
        {
            if (User.IsSuperAdmin())
            {
                return;
            }

            if (RequestsPrivilegedAdministrativeRole(model))
            {
                ModelState.AddModelError(
                    nameof(model.Role),
                    "تعيين الأدوار الإدارية العليا متاح فقط لمالك النظام."
                );
            }

            if (RequestsPrivilegedAdministrativePermissions(model))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "منح صلاحيات إدارة المستخدمين أو الأقسام أو الإدارة أو التفويضات متاح فقط لمالك النظام."
                );
            }
        }

        private static bool RequestsPrivilegedAdministrativeRole(UserEditViewModel model)
        {
            return model.IsSuperAdmin || IsPrivilegedAdministrativeRole(model.Role);
        }

        private static bool RequestsPrivilegedAdministrativePermissions(UserEditViewModel model)
        {
            return model.CanManageUsers
                || model.CanManageDepartments
                || model.CanManageAdministration
                || model.CanManageDelegations;
        }

        private static bool IsNationalIdUsername(string username)
        {
            return SaudiNationalIdOrIqamaValidator.IsValid(username);
        }

        private IEnumerable<UserAccount> GetVisibleUsers()
        {
            var users = _userAdminService.GetAllUsers(User.IsSuperAdmin());
            return User.IsSuperAdmin() ? users : users.Where(user => !user.IsSuperAdmin);
        }

        private UserAccount? GetManagedUserAccount(string username)
        {
            return _userAdminService.GetUserAccount(username, User.IsSuperAdmin());
        }

        private string GetCurrentTenantId()
        {
            var tenantId = User.FindFirst(AppClaimTypes.TenantId)?.Value;
            return string.IsNullOrWhiteSpace(tenantId)
                ? TenantDefaults.DefaultTenantId
                : tenantId.Trim();
        }

        private void ApplyTenantScope(UserEditViewModel model)
        {
            model.CanChooseTenant = User.IsSuperAdmin();
            model.TenantId = string.IsNullOrWhiteSpace(model.TenantId)
                ? GetCurrentTenantId()
                : model.TenantId.Trim();

            if (!User.IsSuperAdmin())
            {
                model.TenantId = GetCurrentTenantId();
            }
        }

        private void PopulateTenantOptions(UserEditViewModel model)
        {
            ApplyTenantScope(model);
            var canChooseTenant = User.IsSuperAdmin();
            var tenants = _userAdminService.GetTenants(includeInactive: canChooseTenant);
            if (!canChooseTenant)
            {
                tenants = tenants.Where(tenant =>
                    string.Equals(
                        tenant.TenantId,
                        model.TenantId,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            }

            model.TenantOptions = tenants
                .Select(tenant => new UserTenantOptionViewModel
                {
                    TenantId = tenant.TenantId,
                    Name = tenant.Name,
                    IsActive = tenant.IsActive,
                })
                .ToList();
        }

        private void ValidateTenantSelection(UserEditViewModel model)
        {
            if (!User.IsSuperAdmin())
            {
                return;
            }

            var selectedTenant = model.TenantOptions.FirstOrDefault(tenant =>
                string.Equals(tenant.TenantId, model.TenantId, StringComparison.OrdinalIgnoreCase)
            );
            if (selectedTenant == null)
            {
                ModelState.AddModelError(nameof(model.TenantId), "يرجى اختيار جهة صحيحة.");
            }
            else if (!selectedTenant.IsActive)
            {
                ModelState.AddModelError(
                    nameof(model.TenantId),
                    "لا يمكن ربط المستخدم بجهة موقوفة."
                );
            }
        }

        private IEnumerable<UserActivity> FilterProtectedActivities(
            IEnumerable<UserActivity> activities
        )
        {
            if (User.IsSuperAdmin())
            {
                return activities;
            }

            var protectedUsernames = _userAdminService
                .GetAllUsers(User.IsSuperAdmin())
                .Where(user => user.IsSuperAdmin)
                .Select(user => user.Username)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return activities.Where(activity =>
                !protectedUsernames.Contains(activity.Username)
                && !protectedUsernames.Contains(activity.RecordedBy ?? string.Empty)
            );
        }

        private bool CanManageUser(UserAccount? user)
        {
            return user != null
                && (
                    User.IsSuperAdmin()
                    || (!user.IsSuperAdmin && !IsPrivilegedAdministrativeRole(user.Role))
                );
        }

        private IActionResult RedirectProtectedSuperAdminAccess()
        {
            QueueToastError("الحسابات الإدارية العليا غير متاحة من هذه الشاشة إلا لمالك النظام.");
            return new RedirectToActionResult(nameof(Index), null, null);
        }

        private static bool IsPrivilegedAdministrativeRole(string? role)
        {
            return IsGeneralManagerRole(role)
                || string.Equals(role, AppRoles.SystemAdmin, StringComparison.OrdinalIgnoreCase);
        }

        private void QueueToastSuccess(string? message)
        {
            QueueToast(message, "success");
        }

        private void SetCredentialNotice(
            string title,
            string username,
            string? temporaryPassword = null,
            string? badgeCode = null,
            string? temporaryPin = null,
            string? note = null
        )
        {
            if (
                string.IsNullOrWhiteSpace(title)
                || (
                    string.IsNullOrWhiteSpace(temporaryPassword)
                    && string.IsNullOrWhiteSpace(badgeCode)
                    && string.IsNullOrWhiteSpace(temporaryPin)
                )
            )
            {
                return;
            }

            TempData[CredentialNoticeTitleTempDataKey] = title.Trim();
            TempData[CredentialNoticeUsernameTempDataKey] = (username ?? string.Empty).Trim();
            TempData[CredentialNoticePasswordTempDataKey] = (
                temporaryPassword ?? string.Empty
            ).Trim();
            TempData[CredentialNoticeBadgeCodeTempDataKey] = (badgeCode ?? string.Empty).Trim();
            TempData[CredentialNoticePinTempDataKey] = (temporaryPin ?? string.Empty).Trim();
            TempData[CredentialNoticeNoteTempDataKey] = (note ?? string.Empty).Trim();
        }

        private void PopulateCredentialNoticeViewData()
        {
            ViewData["CredentialNoticeTitle"] = ReadTempDataValue(CredentialNoticeTitleTempDataKey);
            ViewData["CredentialNoticeUsername"] = ReadTempDataValue(
                CredentialNoticeUsernameTempDataKey
            );
            ViewData["CredentialNoticeTemporaryPassword"] = ReadTempDataValue(
                CredentialNoticePasswordTempDataKey
            );
            ViewData["CredentialNoticeBadgeCode"] = ReadTempDataValue(
                CredentialNoticeBadgeCodeTempDataKey
            );
            ViewData["CredentialNoticeTemporaryPin"] = ReadTempDataValue(
                CredentialNoticePinTempDataKey
            );
            ViewData["CredentialNoticeNote"] = ReadTempDataValue(CredentialNoticeNoteTempDataKey);
        }

        private string ReadTempDataValue(string key)
        {
            return TempData.TryGetValue(key, out var value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
        }

        private void QueueToastError(string? message)
        {
            QueueToast(message, "danger");
        }

        private void QueueToastWarning(string? message)
        {
            QueueToast(message, "warning");
        }

        private void QueueToast(string? message, string type)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var notifications = ReadQueuedToasts();
            var normalizedMessage = message.Trim();
            var normalizedType = NormalizeToastType(type);

            if (
                notifications.Any(notification =>
                    string.Equals(notification.Message, normalizedMessage, StringComparison.Ordinal)
                    && string.Equals(
                        notification.Type,
                        normalizedType,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                WriteQueuedToasts(notifications);
                return;
            }

            notifications.Add(
                new QueuedToastNotification(
                    Guid.NewGuid().ToString("N"),
                    normalizedMessage,
                    normalizedType
                )
            );

            WriteQueuedToasts(notifications);
        }

        private List<QueuedToastNotification> ReadQueuedToasts()
        {
            var payload = TempData.Peek(ToastTempDataKey) as string;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new List<QueuedToastNotification>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<QueuedToastNotification>>(
                        payload,
                        ToastSerializerOptions
                    ) ?? new List<QueuedToastNotification>();
            }
            catch (JsonException)
            {
                return new List<QueuedToastNotification>();
            }
        }

        private void WriteQueuedToasts(List<QueuedToastNotification> notifications)
        {
            TempData[ToastTempDataKey] = JsonSerializer.Serialize(
                notifications,
                ToastSerializerOptions
            );
        }

        private sealed record QueuedToastNotification(string Id, string Message, string Type);

        private static string NormalizeToastType(string type)
        {
            return string.Equals(type, "error", StringComparison.OrdinalIgnoreCase) ? "danger"
                : string.IsNullOrWhiteSpace(type) ? "info"
                : type.Trim().ToLowerInvariant();
        }

    }
}
