using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Models.ViewModels.Tenants;
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Users;

namespace VehiclePermitSystemWeb.Controllers
{
    [AllowAnonymous]
    public sealed class SubscriptionController : Controller
    {
        private readonly ITenantManagementService _tenantManagementService;
        private readonly SignupAttemptGuard? _signupAttemptGuard;
        private readonly IExternalLoginService? _externalLoginService;

        public SubscriptionController(
            ITenantManagementService tenantManagementService,
            SignupAttemptGuard? signupAttemptGuard = null,
            IExternalLoginService? externalLoginService = null
        )
        {
            _tenantManagementService = tenantManagementService;
            _signupAttemptGuard = signupAttemptGuard;
            _externalLoginService = externalLoginService;
        }

        [HttpGet]
        public IActionResult Plans(string? plan = null)
        {
            return View(
                new TenantSignupLandingViewModel
                {
                    Plans = TenantPlanCatalog.GetPlans(),
                    SelectedPlanCode = (plan ?? string.Empty).Trim(),
                }
            );
        }

        [HttpGet]
        public IActionResult Register(string? plan = null)
        {
            var selectedPlan = TenantPlanCatalog.Find(plan) ?? TenantPlanCatalog.GetPlans().First();
            return View(
                new TenantSignupViewModel
                {
                    PlanCode = selectedPlan.Code,
                    Plans = TenantPlanCatalog.GetPlans(),
                    OwnerFullName = TempData.Peek("ExternalSignupName") as string ?? string.Empty,
                    OwnerEmail = TempData.Peek("ExternalSignupEmail") as string ?? string.Empty,
                }
            );
        }

        [HttpGet]
        public async Task<IActionResult> ExternalSignup(
            string provider,
            string? returnMode = null,
            string? plan = null
        )
        {
            var normalizedProvider = ExternalAuthenticationDefaults.NormalizeProvider(provider);
            var schemeProvider = HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            if (
                normalizedProvider == null
                || await schemeProvider.GetSchemeAsync(normalizedProvider) == null
            )
            {
                TempData["SubscriptionNotice"] =
                    "خيار التسجيل المطلوب غير مفعّل بعد. أكمل التسجيل بالبيانات الأساسية أو فعّل مفاتيح مزود الهوية.";
                return string.Equals(returnMode, "Register", StringComparison.OrdinalIgnoreCase)
                    ? RedirectToAction(nameof(Register), new { plan })
                    : RedirectToAction(nameof(Plans), new { plan });
            }

            var callbackUrl = Url.Action(
                nameof(ExternalSignupCallback),
                new { provider = normalizedProvider, returnMode, plan }
            );
            var properties = new AuthenticationProperties { RedirectUri = callbackUrl };
            return Challenge(properties, normalizedProvider);
        }

        [HttpGet]
        public async Task<IActionResult> ExternalSignupCallback(
            string provider,
            string? returnMode = null,
            string? plan = null
        )
        {
            var externalResult = await HttpContext.AuthenticateAsync(
                ExternalAuthenticationDefaults.CookieScheme
            );
            if (!externalResult.Succeeded || externalResult.Principal == null)
            {
                TempData["SubscriptionNotice"] =
                    "تعذر إكمال التسجيل عبر الحساب الخارجي. حاول مرة أخرى أو استخدم التسجيل المعتاد.";
                return RedirectToAction(nameof(Register), new { plan });
            }

            var principal = externalResult.Principal;
            var identity = _externalLoginService?.ReadIdentity(principal, provider);
            var email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            var name = principal.FindFirstValue(ClaimTypes.Name)
                ?? principal.FindFirstValue("name")
                ?? string.Empty;

            await HttpContext.SignOutAsync(ExternalAuthenticationDefaults.CookieScheme);
            if (identity == null)
            {
                TempData["SubscriptionNotice"] =
                    "تعذر التحقق من هوية الحساب الخارجي. حاول مرة أخرى أو استخدم التسجيل المعتاد.";
                return RedirectToAction(nameof(Register), new { plan });
            }

            TempData["ExternalSignupEmail"] = email.Trim();
            TempData["ExternalSignupName"] = name.Trim();
            TempData["ExternalSignupProvider"] = identity.Provider;
            TempData["ExternalSignupIssuer"] = identity.Issuer;
            TempData["ExternalSignupSubject"] = identity.Subject;
            TempData["SubscriptionNotice"] =
                "تم التحقق من حسابك. أكمل بيانات الجهة، ثم يمكنك الدخول مباشرة بحسابك بصلاحيات محدودة حتى تفعيل الباقة.";

            return string.Equals(returnMode, "Register", StringComparison.OrdinalIgnoreCase)
                ? RedirectToAction(nameof(Register), new { plan })
                : RedirectToAction(nameof(Register), new { plan });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("signup")]
        public IActionResult Register(TenantSignupViewModel model)
        {
            model.PlanCode = (model.PlanCode ?? string.Empty).Trim();
            model.CompanyName = (model.CompanyName ?? string.Empty).Trim();
            model.TenantId = (model.TenantId ?? string.Empty).Trim();
            model.OwnerFullName = (model.OwnerFullName ?? string.Empty).Trim();
            model.OwnerUsername = (model.OwnerUsername ?? string.Empty).Trim();
            model.OwnerPhoneNumber = (model.OwnerPhoneNumber ?? string.Empty).Trim();
            model.OwnerEmail = (model.OwnerEmail ?? string.Empty).Trim();
            model.Website = (model.Website ?? string.Empty).Trim();
            model.Plans = TenantPlanCatalog.GetPlans();

            if (!string.IsNullOrWhiteSpace(model.Website))
            {
                return RedirectToAction(nameof(Plans));
            }

            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (
                _signupAttemptGuard != null
                && !_signupAttemptGuard.CanCreate(remoteIp, out var retryAfter)
            )
            {
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                ModelState.AddModelError(
                    string.Empty,
                    "تم بلوغ الحد المؤقت لإنشاء الحسابات من هذا الاتصال. حاول لاحقًا."
                );
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var externalIdentity = ReadPendingExternalIdentity();
            var existingExternalLogin =
                externalIdentity == null ? null : _externalLoginService?.FindLogin(externalIdentity);
            if (existingExternalLogin != null)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "هذا الحساب الخارجي مرتبط بحساب مسجل مسبقًا. استخدم صفحة تسجيل الدخول."
                );
                return View(model);
            }

            var result = _tenantManagementService.CreateSignup(model);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message);
                return View(model);
            }

            _signupAttemptGuard?.RecordSuccess(remoteIp);
            if (externalIdentity != null && _externalLoginService != null)
            {
                var linkResult = _externalLoginService.Link(
                    externalIdentity,
                    result.TenantId,
                    model.OwnerUsername
                );
                if (linkResult.Succeeded)
                {
                    TempData.Remove("ExternalSignupProvider");
                    TempData.Remove("ExternalSignupIssuer");
                    TempData.Remove("ExternalSignupSubject");
                    TempData.Remove("ExternalSignupEmail");
                    TempData.Remove("ExternalSignupName");
                }
            }

            return RedirectToAction(
                nameof(Checkout),
                new
                {
                    tenant = result.TenantId,
                    token = result.CheckoutToken,
                    provider = externalIdentity?.Provider,
                }
            );
        }

        [HttpGet]
        public IActionResult Checkout(string tenant, string token, string? provider = null)
        {
            var model = _tenantManagementService.GetCheckout(tenant, token);
            if (model == null)
            {
                return NotFound();
            }

            model.ExternalProvider =
                ExternalAuthenticationDefaults.NormalizeProvider(provider) ?? string.Empty;
            return View(model);
        }

        private ExternalIdentity? ReadPendingExternalIdentity()
        {
            var provider = TempData.Peek("ExternalSignupProvider") as string ?? string.Empty;
            var issuer = TempData.Peek("ExternalSignupIssuer") as string ?? string.Empty;
            var subject = TempData.Peek("ExternalSignupSubject") as string ?? string.Empty;
            var email = TempData.Peek("ExternalSignupEmail") as string ?? string.Empty;
            return string.IsNullOrWhiteSpace(provider)
                || string.IsNullOrWhiteSpace(issuer)
                || string.IsNullOrWhiteSpace(subject)
                ? null
                : new ExternalIdentity(provider, issuer, subject, email);
        }
    }
}
