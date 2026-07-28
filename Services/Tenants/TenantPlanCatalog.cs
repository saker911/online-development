using VehiclePermitSystemWeb.Models.ViewModels.Tenants;

namespace VehiclePermitSystemWeb.Services.Tenants
{
    public static class TenantPlanCatalog
    {
        private static readonly IReadOnlyList<TenantPlanViewModel> Plans =
        [
            new TenantPlanViewModel
            {
                Code = "monthly",
                Name = "اشتراك شهري",
                Summary = "النظام كامل بمرونة شهرية ودون التزام طويل.",
                DurationMonths = 1,
                TotalPrice = 49,
                Features =
                [
                    "جميع خدمات النظام",
                    "استخدام مفتوح دون حدود",
                    "الباركود وشاشات البوابات",
                    "رابط مستقل وآمن للجهة",
                ],
            },
            new TenantPlanViewModel
            {
                Code = "six-months",
                Name = "اشتراك 6 أشهر",
                Summary = "النظام كامل لمدة ستة أشهر بسعر أوفر.",
                DurationMonths = 6,
                TotalPrice = 249,
                IsFeatured = true,
                Features =
                [
                    "جميع خدمات النظام",
                    "استخدام مفتوح دون حدود",
                    "الباركود وشاشات البوابات",
                    "رابط مستقل وآمن للجهة",
                ],
            },
            new TenantPlanViewModel
            {
                Code = "annual",
                Name = "اشتراك سنوي",
                Summary = "النظام كامل لمدة سنة بأفضل قيمة.",
                DurationMonths = 12,
                TotalPrice = 449,
                Features =
                [
                    "جميع خدمات النظام",
                    "استخدام مفتوح دون حدود",
                    "الباركود وشاشات البوابات",
                    "رابط مستقل وآمن للجهة",
                ],
            },
        ];

        public static IReadOnlyList<TenantPlanViewModel> GetPlans() => Plans;

        public static TenantPlanViewModel? Find(string? code)
        {
            var normalizedCode = (code ?? string.Empty).Trim();
            normalizedCode = normalizedCode.ToLowerInvariant() switch
            {
                "visitors" => "monthly",
                "operations" => "six-months",
                "enterprise" => "annual",
                _ => normalizedCode,
            };
            return Plans.FirstOrDefault(plan =>
                string.Equals(plan.Code, normalizedCode, StringComparison.OrdinalIgnoreCase)
            );
        }
    }
}
