using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public class VisitCompanion : ITenantScopedEntity
    {
        public int Id { get; set; }

        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string VisitId { get; set; } = string.Empty;

        [Display(Name = "اسم المرافق")]
        [StringLength(128, ErrorMessage = "اسم المرافق يجب ألا يتجاوز 128 حرفًا.")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "رقم الهوية")]
        [StringLength(32, ErrorMessage = "رقم الهوية يجب ألا يتجاوز 32 حرفًا.")]
        [RegularExpression(
            @"^$|^[12]\d{9}$",
            ErrorMessage = SaudiNationalIdOrIqamaValidator.ErrorMessage
        )]
        public string NationalId { get; set; } = string.Empty;

        [Display(Name = "رقم الجوال")]
        [StringLength(32, ErrorMessage = "رقم الجوال يجب ألا يتجاوز 32 حرفًا.")]
        [RegularExpression(
            SaudiMobileNumberValidator.OptionalRegularExpressionPattern,
            ErrorMessage = SaudiMobileNumberValidator.OptionalErrorMessage
        )]
        public string PhoneNumber { get; set; } = string.Empty;

        [Display(Name = "صلة العلاقة")]
        [StringLength(64, ErrorMessage = "صلة العلاقة يجب ألا تتجاوز 64 حرفًا.")]
        public string Relationship { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public DateTime? EntryTime { get; set; }

        public DateTime? ExitTime { get; set; }

        public Visit? Visit { get; set; }

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(FullName)
            && string.IsNullOrWhiteSpace(NationalId)
            && string.IsNullOrWhiteSpace(PhoneNumber)
            && string.IsNullOrWhiteSpace(Relationship);
    }
}
