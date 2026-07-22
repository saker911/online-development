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
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly IUserAdminService _userAdminService;
        private readonly LoginAttemptGuard _loginAttemptGuard;
        private readonly IExternalLoginService? _externalLoginService;

        private IToastNotificationService ToastNotifications =>
            HttpContext.RequestServices.GetRequiredService<IToastNotificationService>();

        private VehiclePermitSystemWeb.Services.Common.ISystemClock SystemClock =>
            HttpContext.RequestServices.GetRequiredService<
                VehiclePermitSystemWeb.Services.Common.ISystemClock
            >();

        public AccountController(
            IUserAdminService userAdminService,
            LoginAttemptGuard? loginAttemptGuard = null,
            IExternalLoginService? externalLoginService = null
        )
        {
            _userAdminService = userAdminService;
            _loginAttemptGuard = loginAttemptGuard ?? new LoginAttemptGuard();
            _externalLoginService = externalLoginService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null, string? tenant = null)
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

            var resolvedTenant = ResolveTenantReference(tenant);
            if (!string.IsNullOrWhiteSpace(tenant) && resolvedTenant == null)
            {
                return NotFound();
            }

            ConfigureTenantLoginView(resolvedTenant, returnUrl);
            return View();
        }

        [HttpGet("/o/{tenant}")]
        [AllowAnonymous]
        public IActionResult TenantEntry(string tenant, string? returnUrl = null)
        {
            if (_userAdminService.IsInitialSetupRequired())
            {
                return RedirectToAction(nameof(InitialSetup));
            }

            var resolvedTenant = ResolveTenantReference(tenant);
            if (resolvedTenant == null)
            {
                return NotFound();
            }

            ConfigureTenantLoginView(resolvedTenant, returnUrl);
            return View(nameof(Login));
        }

        [HttpPost]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(
            string username,
            string password,
            string? tenant = null,
            string? returnUrl = null
        )
        {
            username = (username ?? string.Empty).Trim();
            var resolvedTenant = ResolveTenantReference(tenant);
            if (!string.IsNullOrWhiteSpace(tenant) && resolvedTenant == null)
            {
                return NotFound();
            }

            tenant = resolvedTenant?.TenantId ?? (tenant ?? string.Empty).Trim();
            ConfigureTenantLoginView(resolvedTenant, returnUrl);
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            if (_loginAttemptGuard.IsBlocked(username, tenant, remoteIp, out var retryAfter))
            {
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                Response.Headers.RetryAfter = Math.Max(1, (int)retryAfter.TotalSeconds).ToString();
                ViewData["SubmittedUsername"] = username;
                ToastNotifications.Error("تم إيقاف محاولات الدخول مؤقتًا. حاول بعد 15 دقيقة.");
                return View();
            }

            if (_userAdminService.IsInitialSetupRequired())
            {
                return RedirectToAction(nameof(InitialSetup));
            }

            if (!IsNationalIdUsername(username))
            {
                _loginAttemptGuard.RecordFailure(username, tenant, remoteIp);
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
                _loginAttemptGuard.Reset(username, tenant, remoteIp);
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
            ConfigureTenantLoginView(resolvedTenant, returnUrl);
            _loginAttemptGuard.RecordFailure(username, tenant, remoteIp);
            ToastNotifications.Error("اسم المستخدم أو كلمة المرور غير صحيحة");
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> ExternalLogin(
            string provider,
            string? tenant = null,
            string? returnUrl = null
        )
        {
            var normalizedProvider = ExternalAuthenticationDefaults.NormalizeProvider(provider);
            var schemeProvider = HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            if (
                normalizedProvider == null
                || await schemeProvider.GetSchemeAsync(normalizedProvider) == null
            )
            {
                ToastNotifications.Warning(
                    "خيار الدخول المطلوب غير مفعّل لهذه النسخة. استخدم بيانات الحساب أو فعّل مفاتيح مزود الهوية."
                );
                return RedirectToAction(nameof(Login), new { tenant, returnUrl });
            }

            var callbackUrl = Url.Action(
                nameof(ExternalLoginCallback),
                new { provider = normalizedProvider, tenant, returnUrl }
            );
            return Challenge(
                new AuthenticationProperties { RedirectUri = callbackUrl },
                normalizedProvider
            );
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ExternalLoginCallback(
            string provider,
            string? tenant = null,
            string? returnUrl = null
        )
        {
            var externalResult = await HttpContext.AuthenticateAsync(
                ExternalAuthenticationDefaults.CookieScheme
            );
            if (!externalResult.Succeeded || externalResult.Principal == null)
            {
                ToastNotifications.Error(
                    "تعذر إكمال الدخول عبر الحساب الخارجي. حاول مرة أخرى أو استخدم بيانات الحساب."
                );
                return RedirectToAction(nameof(Login), new { tenant, returnUrl });
            }

            var principal = externalResult.Principal;
            var identity = _externalLoginService?.ReadIdentity(principal, provider);
            await HttpContext.SignOutAsync(ExternalAuthenticationDefaults.CookieScheme);

            if (identity == null)
            {
                ToastNotifications.Error("تعذر قراءة المعرّف الأمني الثابت من مزود الهوية.");
                return RedirectToAction(nameof(Login), new { tenant, returnUrl });
            }

            var tenantKey = (tenant ?? string.Empty).Trim();
            var tenants = _userAdminService.GetTenants(includeInactive: true).ToList();
            var matchingTenantIds = string.IsNullOrWhiteSpace(tenantKey)
                ? null
                : tenants
                    .Where(item =>
                        string.Equals(item.TenantId, tenantKey, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(item.Slug, tenantKey, StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(item => item.TenantId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var mappedLogin = _externalLoginService?.FindLogin(
                identity,
                matchingTenantIds?.Count == 1 ? matchingTenantIds.Single() : null
            );
            if (mappedLogin == null)
            {
                ToastNotifications.Error(
                    "هذا الحساب الخارجي غير مرتبط بعد. سجل الدخول بكلمة المرور ثم اربطه من صفحة بياناتي."
                );
                return RedirectToAction(nameof(Login), new { tenant, returnUrl });
            }

            if (matchingTenantIds != null && !matchingTenantIds.Contains(mappedLogin.TenantId))
            {
                return NotFound();
            }

            var user = _userAdminService
                .GetAllUsers(ignoreTenantFilters: true)
                .SingleOrDefault(item =>
                    item.IsActive
                    && item.TenantId == mappedLogin.TenantId
                    && item.Username == mappedLogin.Username
                );
            if (user == null)
            {
                ToastNotifications.Error("الحساب المرتبط غير نشط أو لم يعد موجودًا.");
                return RedirectToAction(nameof(Login), new { tenant, returnUrl });
            }
            var userTenant = tenants.FirstOrDefault(item =>
                string.Equals(item.TenantId, user.TenantId, StringComparison.OrdinalIgnoreCase)
            );
            if (userTenant == null || !IsTenantAvailableForLogin(userTenant))
            {
                ToastNotifications.Error("اشتراك الجهة غير نشط حاليًا. راجع حالة الباقة أو السداد.");
                return RedirectToAction(nameof(Login), new { tenant, returnUrl });
            }

            await SignInUserAsync(user, user.Username, user.DisplayName, user.Role);
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToDefaultAuthorizedPage(user);
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
            var tenantId = User?.FindFirst(AppClaimTypes.TenantId)?.Value;
            var tenantSlug = ResolveTenantReference(tenantId)?.Slug;
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
            return !string.IsNullOrWhiteSpace(tenantSlug)
                ? RedirectToAction(nameof(TenantEntry), new { tenant = tenantSlug })
                : RedirectToAction(nameof(Login));
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
                LinkedExternalProviders = _externalLoginService
                    ?.GetLinkedProviders(user.TenantId, user.Username)
                    .ToHashSet(StringComparer.Ordinal)
                    ?? new HashSet<string>(StringComparer.Ordinal),
            };

            return View(model);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> LinkExternalLogin(string provider)
        {
            var normalizedProvider = ExternalAuthenticationDefaults.NormalizeProvider(provider);
            var schemeProvider = HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            if (
                normalizedProvider == null
                || await schemeProvider.GetSchemeAsync(normalizedProvider) == null
            )
            {
                ToastNotifications.Warning("مزود الهوية المطلوب غير مفعّل حاليًا.");
                return RedirectToAction(nameof(Profile));
            }

            var callbackUrl = Url.Action(
                nameof(LinkExternalLoginCallback),
                new { provider = normalizedProvider }
            );
            var user = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var properties = new AuthenticationProperties { RedirectUri = callbackUrl };
            properties.Items["link-tenant"] = user.TenantId;
            properties.Items["link-username"] = user.Username;
            return Challenge(
                properties,
                normalizedProvider
            );
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> LinkExternalLoginCallback(string provider)
        {
            var externalResult = await HttpContext.AuthenticateAsync(
                ExternalAuthenticationDefaults.CookieScheme
            );
            if (!externalResult.Succeeded || externalResult.Principal == null)
            {
                ToastNotifications.Error("تعذر إكمال ربط الحساب الخارجي.");
                return RedirectToAction(nameof(Profile));
            }

            var identity = _externalLoginService?.ReadIdentity(externalResult.Principal, provider);
            var linkItems = externalResult.Properties?.Items;
            var tenantId = linkItems != null
                && linkItems.TryGetValue("link-tenant", out var storedTenantId)
                    ? storedTenantId ?? string.Empty
                    : string.Empty;
            var username = linkItems != null
                && linkItems.TryGetValue("link-username", out var storedUsername)
                    ? storedUsername ?? string.Empty
                    : string.Empty;
            await HttpContext.SignOutAsync(ExternalAuthenticationDefaults.CookieScheme);
            var user = _userAdminService
                .GetAllUsers(ignoreTenantFilters: true)
                .SingleOrDefault(item =>
                    item.IsActive
                    && item.TenantId == tenantId
                    && item.Username == username
                );
            if (identity == null || user == null || _externalLoginService == null)
            {
                ToastNotifications.Error("تعذر التحقق من هوية الحساب الخارجي.");
                return RedirectToAction(nameof(Profile));
            }

            var result = _externalLoginService.Link(identity, tenantId, username);
            if (result.Succeeded)
            {
                ToastNotifications.Success(result.Message);
            }
            else
            {
                ToastNotifications.Error(result.Message);
            }

            return RedirectToAction(nameof(Profile));
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UnlinkExternalLogin(string provider)
        {
            var user = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            if (user == null || _externalLoginService == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var result = _externalLoginService.Unlink(provider, user.TenantId, user.Username);
            if (result.Succeeded)
            {
                ToastNotifications.Success(result.Message);
            }
            else
            {
                ToastNotifications.Error(result.Message);
            }

            return RedirectToAction(nameof(Profile));
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
            if (
                user != null
                && string.Equals(user.Role, AppRoles.GateSecurity, StringComparison.Ordinal)
                && user.CanScanOperations
            )
            {
                return RedirectToAction("Index", "ScanConsole");
            }

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
            Response.Cookies.Append(
                "TenantId",
                user?.TenantId ?? TenantDefaults.DefaultTenantId,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = Request.IsHttps,
                    IsEssential = true,
                }
            );
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal
            );
        }

        private Tenant? ResolveTenantReference(string? tenantReference)
        {
            var key = (tenantReference ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                key = TenantDefaults.DefaultTenantId;
            }

            return _userAdminService
                .GetTenants(includeInactive: true)
                .FirstOrDefault(tenant =>
                    string.Equals(tenant.TenantId, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tenant.Slug, key, StringComparison.OrdinalIgnoreCase)
                );
        }

        private void ConfigureTenantLoginView(Tenant? tenant, string? returnUrl)
        {
            if (tenant != null)
            {
                HttpContext.Items[HttpTenantContext.ResolvedTenantItemKey] = tenant.TenantId;
            }

            ViewData["ReturnUrl"] = returnUrl;
            ViewData["Tenant"] = tenant?.TenantId ?? string.Empty;
            ViewData["TenantName"] = tenant?.Name ?? string.Empty;
            ViewData["TenantSlug"] = tenant?.Slug ?? string.Empty;
        }

        private static bool IsTenantAvailableForLogin(Tenant tenant)
        {
            if (!tenant.IsActive)
            {
                return false;
            }

            var status = TenantSubscriptionStatuses.Normalize(tenant.SubscriptionStatus);
            if (
                string.Equals(status, TenantSubscriptionStatuses.PendingPayment, StringComparison.Ordinal)
                || string.Equals(status, TenantSubscriptionStatuses.Suspended, StringComparison.Ordinal)
                || string.Equals(status, TenantSubscriptionStatuses.Expired, StringComparison.Ordinal)
            )
            {
                return false;
            }

            var now = DateTime.UtcNow;
            if (
                string.Equals(status, TenantSubscriptionStatuses.Trial, StringComparison.Ordinal)
                && tenant.TrialEndsAtUtc.HasValue
                && tenant.TrialEndsAtUtc.Value < now
            )
            {
                return false;
            }

            return !tenant.SubscriptionEndsAtUtc.HasValue || tenant.SubscriptionEndsAtUtc.Value >= now;
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
