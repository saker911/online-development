using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public class Visit : ITenantScopedEntity
    {
        public const string RequestSourceInternal = "Internal";
        public const string RequestSourcePublicSelfService = "PublicSelfService";
        public const string VisitedPersonTypeHost = "Host";
        public const string VisitedPersonTypeDetained = "Detained";
        public const string VisitedPersonTypePrisoner = "Prisoner";
        public const string VisitedPersonTypeEmployee = "Employee";
        public const string VisitedPersonTypeOther = "Other";

        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string VisitId { get; set; } = string.Empty;

        [Display(Name = "اسم الزائر")]
        [Required(ErrorMessage = "يرجى إدخال اسم الزائر.")]
        [StringLength(128, ErrorMessage = "اسم الزائر يجب ألا يتجاوز 128 حرفًا.")]
        public string VisitorName { get; set; } = string.Empty;

        [Display(Name = "مكان الزيارة")]
        [Required(ErrorMessage = "يرجى إدخال مكان الزيارة.")]
        [StringLength(256, ErrorMessage = "مكان الزيارة يجب ألا يتجاوز 256 حرفًا.")]
        public string VisitLocation { get; set; } = string.Empty;

        [Display(Name = "رقم الهوية")]
        [Required(ErrorMessage = "يرجى إدخال رقم الهوية.")]
        [RegularExpression(
            SaudiNationalIdOrIqamaValidator.RegularExpressionPattern,
            ErrorMessage = SaudiNationalIdOrIqamaValidator.ErrorMessage
        )]
        public string NationalId { get; set; } = string.Empty;

        [Display(Name = "رقم الجوال")]
        [Required(ErrorMessage = "يرجى إدخال رقم الجوال.")]
        [RegularExpression(
            SaudiMobileNumberValidator.RegularExpressionPattern,
            ErrorMessage = SaudiMobileNumberValidator.ErrorMessage
        )]
        public string PhoneNumber { get; set; } = string.Empty;

        [Display(Name = "الغرض")]
        [Required(ErrorMessage = "يرجى إدخال الغرض من الزيارة.")]
        [StringLength(256, ErrorMessage = "الغرض من الزيارة يجب ألا يتجاوز 256 حرفًا.")]
        public string Purpose { get; set; } = string.Empty;

        [Display(Name = "اسم المضيف")]
        [StringLength(128, ErrorMessage = "اسم المضيف يجب ألا يتجاوز 128 حرفًا.")]
        public string HostName { get; set; } = string.Empty;

        [Display(Name = "اسم الشخص المُزار")]
        [Required(ErrorMessage = "يرجى إدخال اسم الشخص المُزار.")]
        [StringLength(128, ErrorMessage = "اسم الشخص المُزار يجب ألا يتجاوز 128 حرفًا.")]
        public string VisitedPersonName { get; set; } = string.Empty;

        [Display(Name = "صفة الشخص المُزار")]
        [Required(ErrorMessage = "يرجى تحديد صفة الشخص المُزار.")]
        [StringLength(32, ErrorMessage = "صفة الشخص المُزار يجب ألا تتجاوز 32 حرفًا.")]
        public string VisitedPersonType { get; set; } = VisitedPersonTypeHost;

        [Display(Name = "موعد الزيارة")]
        public DateTime VisitDate { get; set; }
        public DateTime? EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string Status { get; set; } = "Active"; // Active, Inside, Completed, Suspended
        public string ApprovalStatus { get; set; } = "Pending"; // Pending, Approved, Rejected
        public string RequestSource { get; set; } = RequestSourceInternal;
        public DateTime? RequestedAtUtc { get; set; }

        public List<VisitCompanion> Companions { get; set; } = new();

        public bool IsDetainedVisit =>
            string.Equals(
                VisitedPersonType,
                VisitedPersonTypeDetained,
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                VisitedPersonType,
                VisitedPersonTypePrisoner,
                StringComparison.OrdinalIgnoreCase
            );

        [NotMapped]
        public string SubjectDisplay =>
            string.IsNullOrWhiteSpace(VisitedPersonName) ? HostName : VisitedPersonName;

        [NotMapped]
        public string SubjectLabel => IsDetainedVisit ? "الشخص المُزار" : "الشخص المُزار";

        [NotMapped]
        public string VisitedPersonTypeDisplay =>
            VisitedPersonType switch
            {
                VisitedPersonTypeDetained => "موقوف",
                VisitedPersonTypePrisoner => "نزيل / سجين",
                VisitedPersonTypeEmployee => "موظف",
                VisitedPersonTypeOther => "جهة أخرى",
                _ => "مضيف",
            };

        [NotMapped]
        public IReadOnlyList<VisitCompanion> ActiveCompanions =>
            Companions
                .Where(c => c != null && !c.IsEmpty)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Id)
                .ToList();

        [NotMapped]
        public int CompanionCount => ActiveCompanions.Count;

        [NotMapped]
        public int GroupSize => 1 + CompanionCount;

        [NotMapped]
        public string CompanionSummary =>
            CompanionCount == 0
                ? "لا يوجد مرافقون"
                : string.Join("، ", ActiveCompanions.Select(c => c.FullName));

        public string StatusDisplay =>
            Status == "Active" ? "بانتظار الوصول"
            : Status == "Inside" ? "داخل"
            : Status == "Completed" ? "منتهي"
            : Status == "Suspended" ? "موقوف"
            : "غير معروف";

        public string ApprovalStatusDisplay =>
            ApprovalStatus == "Pending" ? "بانتظار الاعتماد"
            : ApprovalStatus == "Rejected" ? "مرفوض"
            : "معتمد";

        [Display(Name = "مخوّل الاعتماد")]
        public string VisitApproverUsername { get; set; } = string.Empty;
        public DateTime? ArchivedAt { get; set; }
    }
}
