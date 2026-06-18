using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
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
    public class AccountController : Controller
    {
        private readonly IUserAdminService _userAdminService;

        private IToastNotificationService ToastNotifications =>
            HttpContext.RequestServices.GetRequiredService<IToastNotificationService>();

        private VehiclePermitSystemWeb.Services.Common.ISystemClock SystemClock =>
            HttpContext.RequestServices.GetRequiredService<
                VehiclePermitSystemWeb.Services.Common.ISystemClock
            >();

        public AccountController(IUserAdminService userAdminService)
        {
            _userAdminService = userAdminService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (_userAdminService.IsInitialSetupRequired())
            {
                return RedirectToAction(nameof(InitialSetup));
            }

            if (
                TempData["SubmittedUsername"] is string submittedUsername
                && !string.IsNullOrWhiteSpace(submittedUsername)
            )
            {
                ViewData["SubmittedUsername"] = submittedUsername;
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            if (User?.Identity?.IsAuthenticated ?? false)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(
            string username,
            string password,
            string? returnUrl = null
        )
        {
            username = (username ?? string.Empty).Trim();

            if (_userAdminService.IsInitialSetupRequired())
            {
                return RedirectToAction(nameof(InitialSetup));
            }

            if (!IsNationalIdUsername(username))
            {
                ViewData["SubmittedUsername"] = username;
                ToastNotifications.Error("اسم المستخدم يجب أن يكون 10 أرقام.");
                return View();
            }

            if (
                _userAdminService.ValidateCredentials(
                    username,
                    password,
                    out var displayName,
                    out var role
                )
            )
            {
                var user = _userAdminService.GetUserAccount(username);
                await SignInUserAsync(user, username, displayName, role);
                _userAdminService.RecordUserActivity(
                    username,
                    displayName,
                    "Login",
                    "تسجيل دخول",
                    $"تم تسجيل دخول {displayName} بنجاح.",
                    nameof(AccountController),
                    username
                );
                if (user?.MustChangePassword == true)
                {
                    ToastNotifications.Warning(
                        "يجب تغيير كلمة المرور الافتراضية قبل متابعة استخدام النظام."
                    );
                    return RedirectToAction(nameof(ChangePassword), new { forced = true });
                }
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToDefaultAuthorizedPage(user);
            }

            ViewData["SubmittedUsername"] = username;
            ToastNotifications.Error("اسم المستخدم أو كلمة المرور غير صحيحة");
            return View();
        }

        [HttpGet]
        public IActionResult InitialSetup()
        {
            if (!_userAdminService.IsInitialSetupRequired())
            {
                return RedirectToAction(nameof(Login));
            }

            ConfigureInitialSetupView();
            return View(new InitialSetupViewModel { JobTitle = "مالك النظام", CurrentStep = 1 });
        }

        [HttpPost]
        public async Task<IActionResult> InitialSetup(
            InitialSetupViewModel model,
            IFormFile? logoFile
        )
        {
            if (!_userAdminService.IsInitialSetupRequired())
            {
                return RedirectToAction(nameof(Login));
            }

            model.Username = (model.Username ?? string.Empty).Trim();
            model.FullName = (model.FullName ?? string.Empty).Trim();
            model.PhoneNumber = (model.PhoneNumber ?? string.Empty).Trim();
            model.JobTitle = (model.JobTitle ?? string.Empty).Trim();
            model.Password = model.Password ?? string.Empty;
            model.ConfirmPassword = model.ConfirmPassword ?? string.Empty;
            model.OrganizationName = (model.OrganizationName ?? string.Empty).Trim();
            model.AdministrationPhone = (model.AdministrationPhone ?? string.Empty).Trim();
            model.AdministrationEmail = (model.AdministrationEmail ?? string.Empty).Trim();
            model.AdministrationAddress = (model.AdministrationAddress ?? string.Empty).Trim();
            model.CurrentStep = Math.Clamp(model.CurrentStep, 1, 3);

            if (!ModelState.IsValid)
            {
                model.CurrentStep = DetermineInitialSetupStep(model);
                ConfigureInitialSetupView();
                return View(model);
            }

            if (!IsNationalIdUsername(model.Username))
            {
                ModelState.AddModelError(
                    nameof(model.Username),
                    SaudiNationalIdOrIqamaValidator.ErrorMessage
                );
            }

            if (!PasswordValidationRules.IsStrongPassword(model.Password))
            {
                ModelState.AddModelError(
                    nameof(model.Password),
                    "كلمة المرور يجب أن تكون 8 أحرف على الأقل وتحتوي على حرف كبير وحرف صغير ورقم ورمز خاص."
                );
            }

            if (!ModelState.IsValid)
            {
                model.CurrentStep = DetermineInitialSetupStep(model);
                ConfigureInitialSetupView();
                return View(model);
            }

            try
            {
                if (logoFile is { Length: > 0 })
                {
                    model.LogoPath = await AdministrationImageStorage.SaveAsync(
                        logoFile,
                        "logo",
                        SystemClock.UtcNow
                    );
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                model.CurrentStep = 2;
                ConfigureInitialSetupView();
                return View(model);
            }

            if (!_userAdminService.CompleteInitialSetup(model))
            {
                AdministrationImageStorage.DeleteIfManaged(model.LogoPath);
                ToastNotifications.Error(
                    "تعذر إكمال التهيئة الأولى. تأكد من أن النظام لم يُهيأ مسبقًا."
                );
                return RedirectToAction(nameof(Login));
            }

            _userAdminService.RecordUserActivity(
                model.Username,
                model.FullName,
                "InitialSetup",
                "تهيئة أول تشغيل",
                "تم إنشاء حساب مالك النظام الأول وحفظ بيانات الإدارة بنجاح.",
                nameof(AccountController),
                "bootstrap"
            );

            var createdUser = _userAdminService.GetUserAccount(model.Username);
            if (createdUser == null)
            {
                ToastNotifications.Error(
                    "تمت التهيئة ولكن تعذر تحميل الحساب الجديد لتسجيل الدخول التلقائي."
                );
                return RedirectToAction(nameof(Login));
            }

            await SignInUserAsync(
                createdUser,
                createdUser.Username,
                createdUser.DisplayName,
                createdUser.Role
            );

            return RedirectToAction(nameof(InitialSetupSuccess));
        }

        [Authorize]
        [HttpGet]
        public IActionResult InitialSetupSuccess()
        {
            if (_userAdminService.IsInitialSetupRequired())
            {
                return RedirectToAction(nameof(InitialSetup));
            }

            ViewData["Title"] = "اكتملت التهيئة";
            ViewData["HideShell"] = true;
            ViewData["BodyClass"] = "login-page-body";
            return View();
        }

        [HttpPost]
        [ActionName("Logout")]
        public async Task<IActionResult> LogoutPost()
        {
            // remove server-side session if exists
            var sessionId = User?.Claims?.FirstOrDefault(c => c.Type == "sessionId")?.Value;
            var username = User?.Identity?.Name ?? string.Empty;
            var displayName =
                User?.Claims?.FirstOrDefault(c => c.Type == "DisplayName")?.Value ?? username;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _userAdminService.RemoveSession(sessionId);
            }
            if (!string.IsNullOrWhiteSpace(username))
            {
                _userAdminService.RecordUserActivity(
                    username,
                    displayName,
                    "Logout",
                    "تسجيل خروج",
                    $"تم تسجيل خروج {displayName}.",
                    nameof(AccountController),
                    username
                );
            }
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        [Authorize]
        [HttpGet]
        public IActionResult Profile()
        {
            var user = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (user == null)
            {
                return RedirectToAction("Login");
            }

            var grantedPermissionKeys = AppPermissions
                .GetGrantedPermissions(user)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var managerAccount = string.IsNullOrWhiteSpace(user.ManagerUsername)
                ? null
                : _userAdminService.GetUserAccount(user.ManagerUsername);

            var model = new AccountProfileViewModel
            {
                Username = user.Username,
                DisplayName = user.DisplayName,
                FullName = user.FullName,
                RoleDisplayName = AppRoles.GetDisplayName(user),
                Department = user.Department,
                JobTitle = user.JobTitle,
                PhoneNumber = user.PhoneNumber,
                Email = user.Email,
                IsActive = user.IsActive,
                ManagerUsername = user.ManagerUsername,
                ManagerDisplayName = managerAccount?.DisplayName ?? user.ManagerUsername,
                MustChangePassword = user.MustChangePassword,
                MustChangeOperatorPin = user.MustChangeOperatorPin,
                PermissionDisplayNames = grantedPermissionKeys
                    .Select(AppPermissions.GetDisplayName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCulture)
                    .ToList(),
                GrantedPermissionKeys = grantedPermissionKeys.ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                ),
                CanScanOperations = user.CanScanOperations,
                OperatorBadgeCode = user.OperatorBadgeCode,
            };

            return View(model);
        }

        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword(bool forced = false)
        {
            ConfigureChangePasswordView(forced);
            return View(new ChangePasswordViewModel());
        }

        [Authorize]
        [HttpPost]
        public IActionResult ChangePassword(ChangePasswordViewModel model)
        {
            ConfigureChangePasswordView();

            if (string.IsNullOrWhiteSpace(model.CurrentPassword))
            {
                ModelState.AddModelError(string.Empty, "كلمة المرور الحالية مطلوبة.");
            }

            if (string.IsNullOrWhiteSpace(model.NewPassword))
            {
                ModelState.AddModelError(string.Empty, "كلمة المرور الجديدة مطلوبة.");
            }

            if (
                !string.IsNullOrWhiteSpace(model.NewPassword)
                && !PasswordValidationRules.IsStrongPassword(model.NewPassword)
            )
            {
                ModelState.AddModelError(
                    nameof(model.NewPassword),
                    "كلمة المرور الجديدة يجب أن تكون 8 أحرف على الأقل وتحتوي على حرف كبير وحرف صغير ورقم ورمز خاص."
                );
            }

            if (!string.Equals(model.NewPassword, model.ConfirmPassword, StringComparison.Ordinal))
            {
                ModelState.AddModelError(string.Empty, "تأكيد كلمة المرور غير مطابق.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (
                !_userAdminService.ChangePassword(
                    User.Identity?.Name ?? string.Empty,
                    model.CurrentPassword,
                    model.NewPassword
                )
            )
            {
                ModelState.AddModelError(
                    string.Empty,
                    "تعذر تغيير كلمة المرور. تأكد من كلمة المرور الحالية."
                );
                return View(model);
            }

            var username = User.Identity?.Name ?? string.Empty;
            var displayName =
                User.Claims.FirstOrDefault(c => c.Type == "DisplayName")?.Value ?? username;
            _userAdminService.RecordUserActivity(
                username,
                displayName,
                "ChangePassword",
                "تغيير كلمة المرور",
                "تم تغيير كلمة المرور بنجاح.",
                nameof(AccountController),
                username
            );

            ToastNotifications.Success("تم تغيير كلمة المرور بنجاح.");
            return RedirectToDefaultAuthorizedPage(
                _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty)
            );
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetMyTemporaryOperatorPin()
        {
            var username = User.Identity?.Name ?? string.Empty;
            var user = _userAdminService.GetUserAccount(username);
            if (user == null)
            {
                ToastNotifications.Error("تعذر العثور على بيانات الحساب الحالي.");
                return RedirectToAction(nameof(Profile));
            }

            if (!user.CanScanOperations)
            {
                ToastNotifications.Error("هذا الحساب لا يملك صلاحية المسح وتشغيل البوابة.");
                return RedirectToAction(nameof(Profile));
            }

            var badgeCode = _userAdminService.EnsureOperatorBadgeCode(
                user.Username,
                user.OperatorBadgeCode
            );
            var generatedTemporaryPin = UserAccountService.GenerateTemporaryOperatorPin();
            var temporaryPin = _userAdminService.ConfigureOperatorCredentials(
                user.Username,
                badgeCode,
                generatedTemporaryPin,
                true
            );

            if (string.IsNullOrWhiteSpace(temporaryPin))
            {
                ToastNotifications.Error("تعذر إعادة تعيين PIN المؤقت لمشغل البوابة.");
                return RedirectToAction(nameof(Profile));
            }

            _userAdminService.RecordUserActivity(
                user.Username,
                user.DisplayName,
                "ResetMyTemporaryOperatorPin",
                "إعادة تعيين PIN المؤقت لحسابي",
                $"تمت إعادة تعيين PIN مؤقت للحساب الحالي مع إجباره على التغيير عند أول دخول تشغيلي. باركود المشغل: {badgeCode}.",
                nameof(AccountController),
                username
            );

            ToastNotifications.Success(
                $"تمت إعادة تعيين PIN المؤقت بنجاح. PIN الحالي هو {temporaryPin} وباركود المشغل هو {badgeCode}."
            );
            return RedirectToAction(nameof(Profile));
        }

        private IActionResult RedirectToDefaultAuthorizedPage(UserAccount? user)
        {
            if (user?.CanViewDashboard == true)
            {
                return RedirectToAction("Index", "Home");
            }

            if (user?.CanViewPermits == true)
            {
                return RedirectToAction("Index", "Permits");
            }

            if (user?.CanViewVisits == true)
            {
                return RedirectToAction("Index", "Visits");
            }

            if (user?.CanScanOperations == true)
            {
                return RedirectToAction("Index", "ScanConsole");
            }

            if (user?.CanViewDisplays == true)
            {
                return RedirectToAction("Access", "Display");
            }

            return RedirectToAction(nameof(Profile));
        }

        private async Task SignInUserAsync(
            UserAccount? user,
            string username,
            string displayName,
            string role
        )
        {
            var sessionId = _userAdminService.CreateSession(username);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role),
                new Claim("DisplayName", displayName),
                new Claim("RoleDisplayName", AppRoles.GetDisplayName(user)),
                new Claim("sessionId", sessionId),
                new Claim(AppClaimTypes.TenantId, user?.TenantId ?? TenantDefaults.DefaultTenantId),
            };

            if (user?.IsSuperAdmin == true)
            {
                claims.Add(new Claim(AppClaimTypes.SuperAdmin, "true"));
            }

            if (user != null)
            {
                foreach (var permission in AppPermissions.GetGrantedPermissions(user))
                {
                    claims.Add(new Claim(AppPermissions.ClaimType, permission));
                }
            }

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme
            );
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal
            );
        }

        private void ConfigureChangePasswordView(bool forced = false)
        {
            var user = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            var isForced = forced || user?.MustChangePassword == true;
            ViewData["ForcePasswordChange"] = isForced;
            if (isForced)
            {
                ViewData["HideShell"] = true;
            }
        }

        private void ConfigureInitialSetupView()
        {
            ViewData["Title"] = "تهيئة أول تشغيل";
            ViewData["HideShell"] = true;
            ViewData["BodyClass"] = "login-page-body";
        }

        private static int DetermineInitialSetupStep(InitialSetupViewModel model)
        {
            if (
                string.IsNullOrWhiteSpace(model.Username)
                || string.IsNullOrWhiteSpace(model.FullName)
                || string.IsNullOrWhiteSpace(model.PhoneNumber)
                || string.IsNullOrWhiteSpace(model.JobTitle)
                || string.IsNullOrWhiteSpace(model.Password)
                || string.IsNullOrWhiteSpace(model.ConfirmPassword)
                || !string.Equals(model.Password, model.ConfirmPassword, StringComparison.Ordinal)
            )
            {
                return 1;
            }

            if (
                string.IsNullOrWhiteSpace(model.OrganizationName)
                || string.IsNullOrWhiteSpace(model.AdministrationPhone)
                || string.IsNullOrWhiteSpace(model.AdministrationEmail)
                || string.IsNullOrWhiteSpace(model.AdministrationAddress)
            )
            {
                return 2;
            }

            return Math.Clamp(model.CurrentStep, 1, 3);
        }

        private static bool IsNationalIdUsername(string username)
        {
            return SaudiNationalIdOrIqamaValidator.IsValid(username);
        }
    }
}
