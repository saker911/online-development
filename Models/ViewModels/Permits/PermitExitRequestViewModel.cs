using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Permits
{
    public class PermitExitRequestViewModel : IValidatableObject
    {
        public const string RequestModeOneTime = "OneTime";
        public const string RequestModeDailySchedule = "DailySchedule";
        public const string ExitModeWithReturn = "WithReturn";
        public const string ExitModeWithoutReturn = "WithoutReturn";

        public string PermitNumber { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string NationalId { get; set; } = string.Empty;
        public string PermitTypeDisplay { get; set; } = string.Empty;
        public string CurrentStateDisplay { get; set; } = string.Empty;
        public string ApprovalStatusDisplay { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string VehicleType { get; set; } = string.Empty;
        public string PlateNumberDisplay { get; set; } = string.Empty;
        public DateTime? PermitDate { get; set; }

        [Display(Name = "نوع الطلب")]
        [Required(ErrorMessage = "يرجى اختيار نوع الطلب.")]
        public string RequestMode { get; set; } = RequestModeOneTime;

        [Display(Name = "نوع الخروج")]
        [Required(ErrorMessage = "يرجى اختيار نوع الخروج.")]
        public string ExitMode { get; set; } = ExitModeWithReturn;

        [Display(Name = "بداية الاستئذان")]
        public DateTime? LeaveStartAt { get; set; }

        [Display(Name = "نهاية الاستئذان")]
        public DateTime? LeaveEndAt { get; set; }

        [Display(Name = "من تاريخ")]
        public DateTime? ScheduleStartDate { get; set; }

        [Display(Name = "إلى تاريخ")]
        public DateTime? ScheduleEndDate { get; set; }

        [Display(Name = "وقت الخروج اليومي")]
        public TimeSpan? DailyExitTime { get; set; }

        [Display(Name = "وقت العودة اليومي")]
        public TimeSpan? DailyReturnTime { get; set; }

        [Display(Name = "سبب الخروج")]
        [Required(ErrorMessage = "يرجى إدخال سبب الخروج.")]
        [StringLength(256, ErrorMessage = "سبب الخروج يجب ألا يتجاوز 256 حرفًا.")]
        public string LeaveReason { get; set; } = string.Empty;

        public bool RequiresReturn =>
            string.Equals(ExitMode, ExitModeWithReturn, StringComparison.OrdinalIgnoreCase);

        public bool IsDailyScheduleRequest =>
            string.Equals(
                RequestMode,
                RequestModeDailySchedule,
                StringComparison.OrdinalIgnoreCase
            );

        public int? DailyExitMinutes =>
            DailyExitTime.HasValue ? (int)DailyExitTime.Value.TotalMinutes : null;

        public int? DailyReturnMinutes =>
            DailyReturnTime.HasValue ? (int)DailyReturnTime.Value.TotalMinutes : null;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (IsDailyScheduleRequest)
            {
                if (!ScheduleStartDate.HasValue)
                {
                    yield return new ValidationResult(
                        "يرجى تحديد تاريخ بداية الجدولة.",
                        new[] { nameof(ScheduleStartDate) }
                    );
                }

                if (!ScheduleEndDate.HasValue)
                {
                    yield return new ValidationResult(
                        "يرجى تحديد تاريخ نهاية الجدولة.",
                        new[] { nameof(ScheduleEndDate) }
                    );
                }

                if (
                    ScheduleStartDate.HasValue
                    && ScheduleEndDate.HasValue
                    && ScheduleEndDate.Value.Date < ScheduleStartDate.Value.Date
                )
                {
                    yield return new ValidationResult(
                        "يجب أن يكون تاريخ نهاية الجدولة في نفس يوم البداية أو بعده.",
                        new[] { nameof(ScheduleEndDate) }
                    );
                }

                if (!DailyExitTime.HasValue)
                {
                    yield return new ValidationResult(
                        "يرجى تحديد وقت الخروج اليومي.",
                        new[] { nameof(DailyExitTime) }
                    );
                }

                if (RequiresReturn && !DailyReturnTime.HasValue)
                {
                    yield return new ValidationResult(
                        "يرجى تحديد وقت العودة اليومي.",
                        new[] { nameof(DailyReturnTime) }
                    );
                }

                if (
                    RequiresReturn
                    && DailyExitTime.HasValue
                    && DailyReturnTime.HasValue
                    && DailyReturnTime <= DailyExitTime
                )
                {
                    yield return new ValidationResult(
                        "يجب أن يكون وقت العودة اليومي بعد وقت الخروج اليومي.",
                        new[] { nameof(DailyReturnTime) }
                    );
                }
            }
            else
            {
                if (!LeaveStartAt.HasValue)
                {
                    yield return new ValidationResult(
                        "يرجى تحديد بداية الاستئذان.",
                        new[] { nameof(LeaveStartAt) }
                    );
                }

                if (RequiresReturn && !LeaveEndAt.HasValue)
                {
                    yield return new ValidationResult(
                        "يرجى تحديد نهاية الاستئذان.",
                        new[] { nameof(LeaveEndAt) }
                    );
                }

                if (
                    RequiresReturn
                    && LeaveStartAt.HasValue
                    && LeaveEndAt.HasValue
                    && LeaveEndAt <= LeaveStartAt
                )
                {
                    yield return new ValidationResult(
                        "يجب أن تكون نهاية الاستئذان بعد بدايته.",
                        new[] { nameof(LeaveEndAt) }
                    );
                }

                if (LeaveStartAt.HasValue && LeaveStartAt <= AppClock.LocalNow)
                {
                    yield return new ValidationResult(
                        "يجب أن تبدأ بداية الاستئذان من الوقت الفعلي الحالي أو بعده مباشرة.",
                        new[] { nameof(LeaveStartAt) }
                    );
                }

                if (RequiresReturn && LeaveEndAt.HasValue && LeaveEndAt <= AppClock.LocalNow)
                {
                    yield return new ValidationResult(
                        "يجب أن تكون نهاية الاستئذان بعد الوقت الحالي.",
                        new[] { nameof(LeaveEndAt) }
                    );
                }
            }

            if (string.IsNullOrWhiteSpace(LeaveReason))
            {
                yield return new ValidationResult(
                    "يرجى إدخال سبب الخروج.",
                    new[] { nameof(LeaveReason) }
                );
            }
        }
    }
}
