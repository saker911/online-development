using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Platform
{
    public sealed class SubscriptionPricingViewModel : IValidatableObject
    {
        public List<SubscriptionPlanEditorViewModel> Plans { get; set; } = [];
        public string FeaturedPlanCode { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Plans.Count == 0 || Plans.All(plan => !plan.IsActive))
            {
                yield return new ValidationResult("يجب إبقاء باقة واحدة مفعلة على الأقل.");
            }

            if (!string.IsNullOrWhiteSpace(FeaturedPlanCode))
            {
                var featuredPlan = Plans.FirstOrDefault(plan =>
                    string.Equals(plan.Code, FeaturedPlanCode, StringComparison.OrdinalIgnoreCase)
                );
                if (featuredPlan == null || !featuredPlan.IsActive)
                {
                    yield return new ValidationResult(
                        "الباقة المميزة يجب أن تكون من الباقات المفعلة.",
                        [nameof(FeaturedPlanCode)]
                    );
                }
            }

            foreach (var plan in Plans)
            {
                if (plan.OriginalPrice.HasValue && plan.OriginalPrice.Value <= plan.Price)
                {
                    yield return new ValidationResult(
                        $"السعر السابق في باقة {plan.Name} يجب أن يكون أعلى من السعر الحالي.",
                        [$"Plans[{Plans.IndexOf(plan)}].OriginalPrice"]
                    );
                }

                if (
                    plan.OfferStartsAtUtc.HasValue
                    && plan.OfferEndsAtUtc.HasValue
                    && plan.OfferEndsAtUtc.Value <= plan.OfferStartsAtUtc.Value
                )
                {
                    yield return new ValidationResult(
                        $"نهاية عرض باقة {plan.Name} يجب أن تكون بعد بدايته.",
                        [$"Plans[{Plans.IndexOf(plan)}].OfferEndsAtUtc"]
                    );
                }
            }
        }
    }

    public sealed class SubscriptionPlanEditorViewModel
    {
        [Required]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "اسم الباقة مطلوب.")]
        [StringLength(96)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "وصف الباقة مطلوب.")]
        [StringLength(320)]
        public string Summary { get; set; } = string.Empty;

        [Range(1, 120, ErrorMessage = "مدة الباقة يجب أن تكون بين شهر و120 شهرًا.")]
        public int DurationMonths { get; set; }

        [Range(typeof(decimal), "0", "1000000", ErrorMessage = "أدخل سعرًا صحيحًا.")]
        public decimal Price { get; set; }

        [Range(typeof(decimal), "0", "1000000", ErrorMessage = "أدخل سعرًا سابقًا صحيحًا.")]
        public decimal? OriginalPrice { get; set; }

        [StringLength(64)]
        public string OfferLabel { get; set; } = string.Empty;
        public DateTime? OfferStartsAtUtc { get; set; }
        public DateTime? OfferEndsAtUtc { get; set; }
        public bool IsActive { get; set; }

        [StringLength(2000)]
        public string FeaturesText { get; set; } = string.Empty;
    }
}
