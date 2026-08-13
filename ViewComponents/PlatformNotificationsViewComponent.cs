using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Platform;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Tenants;

namespace VehiclePermitSystemWeb.ViewComponents
{
    public sealed class PlatformNotificationsViewComponent : ViewComponent
    {
        private readonly ITenantManagementService _tenantManagementService;

        public PlatformNotificationsViewComponent(ITenantManagementService tenantManagementService)
        {
            _tenantManagementService = tenantManagementService;
        }

        public IViewComponentResult Invoke()
        {
            var model = new PlatformNotificationCenterViewModel();
            if (!(UserClaimsPrincipal?.IsSuperAdmin() ?? false))
            {
                return View(model);
            }

            var now = DateTime.UtcNow;
            var recentCutoff = now.AddDays(-14);
            var dashboard = _tenantManagementService.GetDashboard();
            var pendingTenantIds = dashboard.Tenants
                .Where(tenant => string.Equals(
                    tenant.SubscriptionStatus,
                    TenantSubscriptionStatuses.PendingPayment,
                    StringComparison.Ordinal
                ))
                .Select(tenant => tenant.TenantId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            model.Items.AddRange(dashboard.Tenants
                .Where(tenant => pendingTenantIds.Contains(tenant.TenantId))
                .OrderByDescending(tenant => tenant.CreatedAtUtc)
                .Select(tenant => new PlatformNotificationItemViewModel
                {
                    Id = $"payment:{tenant.TenantId}:{tenant.CreatedAtUtc.Ticks}",
                    Title = "جهة بانتظار تأكيد الدفع",
                    Message = tenant.Name,
                    Category = "الاشتراكات",
                    Tone = "warning",
                    ActionUrl = Url.Action("Index", "Tenants", new { edit = tenant.TenantId }) ?? "/Tenants",
                    OccurredAtUtc = tenant.CreatedAtUtc,
                }));

            model.Items.AddRange(dashboard.Tenants
                .Where(tenant => tenant.CreatedAtUtc >= recentCutoff)
                .Where(tenant => !string.Equals(
                    tenant.TenantId,
                    TenantDefaults.DefaultTenantId,
                    StringComparison.OrdinalIgnoreCase
                ))
                .Where(tenant => !pendingTenantIds.Contains(tenant.TenantId))
                .OrderByDescending(tenant => tenant.CreatedAtUtc)
                .Select(tenant => new PlatformNotificationItemViewModel
                {
                    Id = $"registration:{tenant.TenantId}:{tenant.CreatedAtUtc.Ticks}",
                    Title = "تسجيل جهة جديدة",
                    Message = tenant.Name,
                    Category = "التسجيلات",
                    Tone = "success",
                    ActionUrl = Url.Action("Index", "Tenants", new { edit = tenant.TenantId }) ?? "/Tenants",
                    OccurredAtUtc = tenant.CreatedAtUtc,
                }));

            model.Items = model.Items
                .OrderByDescending(item => item.OccurredAtUtc)
                .Take(8)
                .ToList();

            return View(model);
        }
    }
}
