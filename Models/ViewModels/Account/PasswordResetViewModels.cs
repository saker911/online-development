using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Account
{
    public sealed class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "أدخل اسم المستخدم أو البريد الإلكتروني أو الجوال.")]
        [Display(Name = "اسم المستخدم أو البريد الإلكتروني أو الجوال")]
        public string AccountIdentifier { get; set; } = string.Empty;

        public string Tenant { get; set; } = string.Empty;
    }

    public sealed class ResetPasswordViewModel
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة.")]
        [DataType(DataType.Password)]
        [Display(Name = "كلمة المرور الجديدة")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "تأكيد كلمة المرور غير مطابق.")]
        [Display(Name = "تأكيد كلمة المرور")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
