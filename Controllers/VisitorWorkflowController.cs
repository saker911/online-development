using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ManageAdministration)]
    public sealed class VisitorWorkflowController : Controller
    {
        private readonly IVisitorWorkflowService _visitorWorkflowService;

        public VisitorWorkflowController(IVisitorWorkflowService visitorWorkflowService)
        {
            _visitorWorkflowService = visitorWorkflowService;
        }

        [HttpGet]
        public IActionResult Index() => View(_visitorWorkflowService.GetSettings());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(VisitorWorkflowSettingsViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _visitorWorkflowService.Update(model);
            this.ToastSuccess("تم حفظ مسار الزيارة وتطبيقه على رابط الزوار.");
            return RedirectToAction(nameof(Index));
        }
    }
}
