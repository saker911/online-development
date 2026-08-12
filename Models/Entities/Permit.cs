using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public class Permit : IValidatableObject, ITenantScopedEntity
    {
        public const string PermitTypeTemporary = "Temporary";
        public const string PermitTypePermanent = "Permanent";
        public const string PermitTypeExit = "Exit";
        public const string PermitTypeGuest = "Guest";
        public const string PermitTypeVisitor = "Visitor";
        public const string AccessModeFullAccess = "FullAccess";
        public const string AccessModeEntryOnly = "EntryOnly";
        public const string ApprovalStatusPendingReview = "PendingReview";
        public const string ApprovalStatusPending = "Pending";

        public string TenantId { get; set; } = TenantDefaults.DefaultTenantId;
        public string PermitNumber { get; set; } = string.Empty;
        public string PermitType { get; set; } = PermitTypeTemporary;
        public bool RequiresReturn { get; set; } = true;
        public string AccessMode { get; set; } = AccessModeFullAccess;

        [Display(Name = "اسم المصرح له")]
        [Required(ErrorMessage = "يرجى إدخال اسم المصرح له.")]
        public string DriverName { get; set; } = string.Empty;

        [Display(Name = "رقم الهوية")]
        [Required(ErrorMessage = "رقم الهوية مطلوب.")]
        [RegularExpression(
            SaudiNationalIdOrIqamaValidator.RegularExpressionPattern,
            ErrorMessage = SaudiNationalIdOrIqamaValidator.ErrorMessage
        )]
        public string NationalId { get; set; } = string.Empty;

        [Display(Name = "نوع المركبة")]
        [Required(ErrorMessage = "يرجى إدخال نوع المركبة.")]
        public string VehicleType { get; set; } = string.Empty;

        public const string PlateOriginSaudi = "Saudi";
        public const string PlateOriginForeign = "Foreign";

        [Display(Name = "نوع اللوحة")]
        public string PlateOrigin { get; set; } = PlateOriginSaudi;

        [Display(Name = "رقم اللوحة")]
        [Required(ErrorMessage = "يرجى إدخال رقم اللوحة.")]
        public string PlateNumber { get; set; } = string.Empty;

        [Display(Name = "نص التصريح")]
        public string Subject { get; set; } = string.Empty;

        [Display(Name = "الجهة المصرحة")]
        public string AuthorizingEntity { get; set; } = string.Empty;

        [Display(Name = "موقع العمل")]
        public string DepartmentName { get; set; } = string.Empty;

        [Display(Name = "مكان الزيارة")]
        public string VisitLocation { get; set; } = string.Empty;

        [Display(Name = "الموقع")]
        public int? WorkplaceSiteId { get; set; }

        [Display(Name = "المدخل")]
        public int? WorkplaceSiteEntranceId { get; set; }

        public WorkplaceSite? WorkplaceSite { get; set; }
        public WorkplaceSiteEntrance? WorkplaceSiteEntrance { get; set; }

        [Display(Name = "اسم الضابط")]
        public string OfficerName { get; set; } = string.Empty;

        [Display(Name = "مدير الموظف")]
        public string ManagerName { get; set; } = string.Empty;

        [Display(Name = "الرقم الوظيفي")]
        public string EmployeeNumber { get; set; } = string.Empty;

        [Display(Name = "إدارة الموظف")]
        public string EmployeeDepartment { get; set; } = string.Empty;

        [Display(Name = "المسمى الوظيفي")]
        public string JobTitle { get; set; } = string.Empty;

        [Display(Name = "رقم الجوال")]
        [Required(ErrorMessage = "يرجى إدخال رقم الجوال.")]
        [RegularExpression(
            SaudiMobileNumberValidator.RegularExpressionPattern,
            ErrorMessage = SaudiMobileNumberValidator.ErrorMessage
        )]
        public string EmployeePhone { get; set; } = string.Empty;

        [Display(Name = "البريد الإلكتروني لحامل التصريح")]
        [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة.")]
        [StringLength(256, ErrorMessage = "البريد الإلكتروني يجب ألا يتجاوز 256 حرفًا.")]
        public string? HolderEmail { get; set; }

        [Display(Name = "تاريخ التصريح")]
        public DateTime? PermitDate { get; set; }

        public DateTime? ExpiresAt { get; set; }
        public DateTime? ExpectedReturnTime { get; set; }
        public DateTime? LeaveWindowStartAt { get; set; }
        public DateTime? LeaveWindowEndAt { get; set; }
        public bool HasDailyLeaveSchedule { get; set; }
        public DateTime? DailyLeaveScheduleStartDate { get; set; }
        public DateTime? DailyLeaveScheduleEndDate { get; set; }
        public int? DailyLeaveScheduleExitMinutes { get; set; }
        public int? DailyLeaveScheduleReturnMinutes { get; set; }
        public bool DailyLeaveScheduleRequiresReturn { get; set; }
        public string DailyLeaveScheduleReason { get; set; } = string.Empty;
        public bool PendingExitRequest { get; set; }
        public string LeaveReason { get; set; } = string.Empty;
        public int LateReturnWarningCount { get; set; }
        public DateTime? LastLateReturnWarningForExpectedReturnTime { get; set; }
        public int UnauthorizedExitWarningCount { get; set; }
        public DateTime? LastUnauthorizedExitWarningAt { get; set; }
        public DateTime? PendingUnauthorizedExitAt { get; set; }
        public string PendingUnauthorizedExitSequenceId { get; set; } = string.Empty;
        public DateTime? LastAutomaticWorkEndExitAt { get; set; }

        // Times recorded by gate scanner
        public DateTime? OutTime { get; set; }
        public DateTime? ReturnTime { get; set; }
        public DateTime? ArchivedAt { get; set; }

        // Status: Pending, Approved, Rejected, Expired, Late, Out, Returned
        public string ApprovalStatus { get; set; } = "Pending"; // default
        public string CurrentState { get; set; } = "Outside";
        public DateTime? TemporaryExitPermissionUntil { get; set; }
        public ICollection<PermitActivity> Activities { get; set; } = new List<PermitActivity>();

        public string ApprovalStatusDisplay =>
            ApprovalStatus == "Approved" ? "معتمد"
            : ApprovalStatus == ApprovalStatusPendingReview ? "قيد التدقيق"
            : ApprovalStatus == ApprovalStatusPending ? "بانتظار اعتماد الأمن"
            : ApprovalStatus == "Rejected" ? "مرفوض"
            : ApprovalStatus == "Stopped" ? "موقوف"
            : ApprovalStatus == "Expired" && ArchivedAt.HasValue ? "منتهي ومؤرشف"
            : ApprovalStatus == "Expired" ? "منتهي"
            : ApprovalStatus == "Late" ? "متأخر"
            : ApprovalStatus == "Exited" ? "خرج بلا عودة"
            : ApprovalStatus == "Out" ? "خارج"
            : ApprovalStatus == "Returned" && ArchivedAt.HasValue ? "عاد وتمت أرشفته"
            : ApprovalStatus == "Returned" ? "عاد"
            : "غير معروف";

        public string PermitTypeDisplay =>
            PermitType == PermitTypePermanent ? "موظف"
            : PermitType == PermitTypeExit ? "تصريح خروج"
            : "زائر";

        public bool IsPermanentPermit =>
            string.Equals(PermitType, PermitTypePermanent, StringComparison.OrdinalIgnoreCase);

        public string EffectiveAccessMode =>
            ResolveEffectiveAccessMode(AccessMode, RequiresReturn, IsPermanentPermit);

        public bool IsFullAccessPermit =>
            IsPermanentPermit
            && string.Equals(
                EffectiveAccessMode,
                AccessModeFullAccess,
                StringComparison.OrdinalIgnoreCase
            );

        public bool IsEntryOnlyPermit =>
            IsPermanentPermit
            && string.Equals(
                EffectiveAccessMode,
                AccessModeEntryOnly,
                StringComparison.OrdinalIgnoreCase
            );

        public bool IsVisitorPermit => IsVisitorPermitType(PermitType);

        public bool RequiresPermitDates => IsPermanentPermit;

        public string ReturnRequirementDisplay =>
            IsPermanentPermit ? (IsFullAccessPermit ? "خروج ودخول كامل" : "دخول فقط")
            : RequiresReturn ? "بعودة"
            : "بدون عودة";

        public bool HasTemporaryExitPermission =>
            TemporaryExitPermissionUntil.HasValue
            && TemporaryExitPermissionUntil.Value > AppClock.LocalNow;

        public string TemporaryExitPermissionDisplay =>
            HasTemporaryExitPermission
                ? $"مفعل حتى {VehiclePermitSystemWeb.Utilities.Dates.HijriDateFormatter.Format(TemporaryExitPermissionUntil)}"
                : "غير مفعل";

        public string PendingExitRequestDisplay =>
            PendingExitRequest ? "طلب استئذان مفتوح"
            : ExpectedReturnTime.HasValue
            && string.Equals(ApprovalStatus, "Out", StringComparison.OrdinalIgnoreCase)
                ? $"خروج مصرح والعودة المتوقعة {HijriDateFormatter.Format(ExpectedReturnTime)}"
            : "لا يوجد طلب";

        public string DailyLeaveScheduleDisplay =>
            !HasDailyLeaveSchedule ? "لا توجد جدولة"
            : DailyLeaveScheduleRequiresReturn ? "خروج وعودة مجدول"
            : "خروج بدون عودة مجدول";

        public string LateReturnWarningDisplay =>
            LateReturnWarningCount > 0 ? $"{LateReturnWarningCount} تنبيه" : "لا توجد تنبيهات";

        public string UnauthorizedExitWarningDisplay =>
            UnauthorizedExitWarningCount > 0
                ? $"{UnauthorizedExitWarningCount} تنبيه"
                : "لا توجد تنبيهات";

        public bool HasPendingUnauthorizedExit =>
            PendingUnauthorizedExitAt.HasValue
            && !string.IsNullOrWhiteSpace(PendingUnauthorizedExitSequenceId);

        public string PendingUnauthorizedExitDisplay =>
            HasPendingUnauthorizedExit
                ? $"محاولة معلقة منذ {HijriDateFormatter.Format(PendingUnauthorizedExitAt)}"
                : "لا توجد محاولات معلقة";

        public string GateActionDescription =>
            IsPermanentPermit
                ? (IsFullAccessPermit ? "خروج ودخول كامل" : "دخول فقط")
                : (RequiresReturn ? "الخروج والعودة" : "الخروج فقط");

        public void NormalizeAccessModeState()
        {
            AccessMode = ResolveEffectiveAccessMode(AccessMode, RequiresReturn, IsPermanentPermit);
            if (IsPermanentPermit)
            {
                RequiresReturn = IsFullAccessPermit;
            }
            else
            {
                AccessMode = AccessModeFullAccess;
            }
        }

        public static string ResolveEffectiveAccessMode(
            string? accessMode,
            bool requiresReturn,
            bool isPermanentPermit
        )
        {
            if (!isPermanentPermit)
            {
                return AccessModeFullAccess;
            }

            if (string.Equals(accessMode, AccessModeEntryOnly, StringComparison.OrdinalIgnoreCase))
            {
                return AccessModeEntryOnly;
            }

            if (
                string.Equals(accessMode, AccessModeFullAccess, StringComparison.OrdinalIgnoreCase)
                && requiresReturn
            )
            {
                return AccessModeFullAccess;
            }

            return requiresReturn ? AccessModeFullAccess : AccessModeEntryOnly;
        }

        public bool RequiresVisitLocation => IsVisitorPermit;

        public string LocationDisplay => RequiresVisitLocation ? VisitLocation : DepartmentName;

        public string PlateNumberDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PlateNumber))
                {
                    return string.Empty;
                }

                var compactValue = PlateNumber
                    .Replace(" ", string.Empty)
                    .Replace("-", string.Empty)
                    .Trim();
                if (compactValue.Length < 7)
                {
                    return PlateNumber;
                }

                var letters = compactValue[..3].ToCharArray();
                var digits = compactValue[3..];
                return $"{string.Join(" ", letters)} {digits}";
            }
        }

        public string PublicPermitCode
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PermitNumber))
                {
                    return string.Empty;
                }

                var trimmed = PermitNumber.Trim();
                if (
                    trimmed.StartsWith("PERMIT-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(trimmed[7..], out var publicValue)
                )
                {
                    return $"PERMIT-{publicValue:D5}";
                }

                if (
                    trimmed.StartsWith("P", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(trimmed[1..], out var numericValue)
                )
                {
                    return $"PERMIT-{numericValue:D5}";
                }

                return trimmed;
            }
        }

        // QR token or payload (optional)
        public string QrToken { get; set; } = string.Empty;

        [Display(Name = "المستخدم المنشئ")]
        public string CreatedBy { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var allowedTypes = new[]
            {
                PermitTypeTemporary,
                PermitTypePermanent,
                PermitTypeExit,
                PermitTypeGuest,
                PermitTypeVisitor,
            };

            if (!allowedTypes.Contains(PermitType, StringComparer.OrdinalIgnoreCase))
            {
                yield return new ValidationResult(
                    "نوع التصريح غير صالح.",
                    new[] { nameof(PermitType) }
                );
            }

            if (RequiresVisitLocation && string.IsNullOrWhiteSpace(VisitLocation))
            {
                yield return new ValidationResult(
                    "يرجى إدخال مكان الزيارة عند اختيار نوع التصريح زائر.",
                    new[] { nameof(VisitLocation) }
                );
            }

            if (!RequiresVisitLocation && string.IsNullOrWhiteSpace(DepartmentName))
            {
                yield return new ValidationResult(
                    "يرجى إدخال موقع العمل.",
                    new[] { nameof(DepartmentName) }
                );
            }

            if (IsPermanentPermit)
            {
                var normalizedAccessMode = ResolveEffectiveAccessMode(
                    AccessMode,
                    RequiresReturn,
                    true
                );
                if (
                    !string.Equals(
                        normalizedAccessMode,
                        AccessModeFullAccess,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !string.Equals(
                        normalizedAccessMode,
                        AccessModeEntryOnly,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    yield return new ValidationResult(
                        "نمط الوصول غير صالح.",
                        new[] { nameof(AccessMode) }
                    );
                }

                if (!PermitDate.HasValue)
                {
                    yield return new ValidationResult(
                        "يرجى إدخال تاريخ التصريح.",
                        new[] { nameof(PermitDate) }
                    );
                }

                if (!ExpiresAt.HasValue)
                {
                    yield return new ValidationResult(
                        "يرجى إدخال تاريخ الانتهاء.",
                        new[] { nameof(ExpiresAt) }
                    );
                }

                if (PermitDate.HasValue && ExpiresAt.HasValue && ExpiresAt <= PermitDate)
                {
                    yield return new ValidationResult(
                        "يجب أن يكون تاريخ الانتهاء بعد تاريخ التصريح.",
                        new[] { nameof(ExpiresAt) }
                    );
                }
            }

            if (string.Equals(PermitType, PermitTypeExit, StringComparison.OrdinalIgnoreCase))
            {
                if (!ExpiresAt.HasValue)
                {
                    yield return new ValidationResult(
                        "يرجى إدخال تاريخ العودة.",
                        new[] { nameof(ExpiresAt) }
                    );
                }

                if (PermitDate.HasValue && ExpiresAt.HasValue && ExpiresAt <= PermitDate)
                {
                    yield return new ValidationResult(
                        "يجب أن يكون تاريخ العودة بعد تاريخ التصريح.",
                        new[] { nameof(ExpiresAt) }
                    );
                }
            }

            // Plate number validation depending on origin
            if (string.IsNullOrWhiteSpace(PlateNumber))
            {
                yield return new ValidationResult(
                    "يرجى إدخال رقم اللوحة.",
                    new[] { nameof(PlateNumber) }
                );
            }

            if (string.Equals(PlateOrigin, PlateOriginSaudi, StringComparison.OrdinalIgnoreCase))
            {
                var compact = (PlateNumber ?? string.Empty)
                    .Replace(" ", string.Empty)
                    .Replace("-", string.Empty)
                    .Trim();
                var re = new System.Text.RegularExpressions.Regex(
                    "^[A-Za-z\u0621-\u064A]{3}[0-9\u0660-\u0669\u06F0-\u06F9]{4}$"
                );
                if (!re.IsMatch(compact))
                {
                    yield return new ValidationResult(
                        "رقم اللوحة السعودية يجب أن يتكون من 3 أحرف و4 أرقام.",
                        new[] { nameof(PlateNumber) }
                    );
                }
            }
        }

        public static bool IsVisitorPermitType(string? permitType)
        {
            return string.Equals(
                    permitType,
                    PermitTypeTemporary,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(permitType, PermitTypeGuest, StringComparison.OrdinalIgnoreCase)
                || string.Equals(permitType, PermitTypeVisitor, StringComparison.OrdinalIgnoreCase);
        }
    }
}
