using System.ComponentModel.DataAnnotations;
using VehiclePermitSystemWeb.Models.Entities;

namespace VehiclePermitSystemWeb.Models.ViewModels.Visits
{
    public sealed class VisitorWorkflowSettingsViewModel : IValidatableObject
    {
        public bool IsEnabled { get; set; } = true;

        [Required]
        public string TemplateKey { get; set; } = VisitorWorkflowTemplates.Standard;

        public bool ShowNationalId { get; set; } = true;
        public bool RequireNationalId { get; set; }
        public bool ShowHostName { get; set; } = true;
        public bool RequireHostName { get; set; } = true;
        public bool ShowVisitLocation { get; set; } = true;
        public bool RequireVisitLocation { get; set; } = true;
        public bool ShowPurpose { get; set; } = true;
        public bool RequirePurpose { get; set; } = true;

        [Range(0, 1440, ErrorMessage = "المدة المسبقة يجب أن تكون بين صفر و1440 دقيقة.")]
        public int MinimumLeadMinutes { get; set; } = 5;

        [Range(1, 90, ErrorMessage = "مدة الحجز المتاحة يجب أن تكون بين يوم و90 يومًا.")]
        public int MaximumAdvanceDays { get; set; } = 30;

        [Required(ErrorMessage = "رسالة الترحيب مطلوبة.")]
        [StringLength(240, ErrorMessage = "رسالة الترحيب يجب ألا تتجاوز 240 حرفًا.")]
        public string WelcomeMessage { get; set; } =
            "أدخل بيانات الزيارة، وسيصل الطلب إلى فريق الزيارات للمراجعة.";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (RequireNationalId && !ShowNationalId)
            {
                yield return RequiredFieldMustBeVisible("رقم الهوية", nameof(ShowNationalId));
            }

            if (RequireHostName && !ShowHostName)
            {
                yield return RequiredFieldMustBeVisible("المضيف", nameof(ShowHostName));
            }

            if (RequireVisitLocation && !ShowVisitLocation)
            {
                yield return RequiredFieldMustBeVisible("مكان الزيارة", nameof(ShowVisitLocation));
            }

            if (RequirePurpose && !ShowPurpose)
            {
                yield return RequiredFieldMustBeVisible("سبب الزيارة", nameof(ShowPurpose));
            }
        }

        public static VisitorWorkflowSettingsViewModel CreateDefault() => new();

        private static ValidationResult RequiredFieldMustBeVisible(
            string fieldName,
            string memberName
        ) => new($"لا يمكن جعل {fieldName} إلزاميًا وهو مخفي.", [memberName]);
    }
}
