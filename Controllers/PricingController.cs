using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.ViewModels.Platform;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Tenants;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize]
    public sealed class PricingController : Controller
    {
        private readonly ISubscriptionPlanService _subscriptionPlanService;

        public PricingController(ISubscriptionPlanService subscriptionPlanService)
        {
            _subscriptionPlanService = subscriptionPlanService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            return View(_subscriptionPlanService.GetEditor());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(SubscriptionPricingViewModel model)
        {
            if (!User.IsSuperAdmin())
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _subscriptionPlanService.Update(model);
            TempData["SuccessMessage"] = "تم تحديث الأسعار والعروض ونشرها في صفحات الاشتراك.";
            return RedirectToAction(nameof(Index));
        }
    }
}
