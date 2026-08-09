using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class VisitorWorkflowSettings : ITenantScopedEntity
    {
        [Key]
        [StringLength(64)]
        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;

        [StringLength(24)]
        public string TemplateKey { get; set; } = VisitorWorkflowTemplates.Standard;

        public bool IsEnabled { get; set; } = true;
        public bool ShowNationalId { get; set; } = true;
        public bool RequireNationalId { get; set; }
        public bool ShowHostName { get; set; } = true;
        public bool RequireHostName { get; set; } = true;
        public bool ShowVisitLocation { get; set; } = true;
        public bool RequireVisitLocation { get; set; } = true;
        public bool ShowPurpose { get; set; } = true;
        public bool RequirePurpose { get; set; } = true;
        public int MinimumLeadMinutes { get; set; } = 5;
        public int MaximumAdvanceDays { get; set; } = 30;

        [StringLength(240)]
        public string WelcomeMessage { get; set; } =
            "أدخل بيانات الزيارة، وسيصل الطلب إلى فريق الزيارات للمراجعة.";

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public static class VisitorWorkflowTemplates
    {
        public const string Express = "Express";
        public const string Standard = "Standard";
        public const string Secure = "Secure";

        public static string Normalize(string? value) => value switch
        {
            Express => Express,
            Secure => Secure,
            _ => Standard,
        };
    }
}
