using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Account
{
    public class InitialSetupViewModel
    {
        [Display(Name = "اسم المستخدم")]
        [Required(ErrorMessage = "اسم المستخدم مطلوب.")]
        [RegularExpression(
            NewAccountUsernameValidator.RegularExpressionPattern,
            ErrorMessage = NewAccountUsernameValidator.ErrorMessage
        )]
        public string Username { get; set; } = string.Empty;

        [Display(Name = "الاسم الكامل")]
        [Required(ErrorMessage = "الاسم الكامل مطلوب.")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "الجوال")]
        [Required(ErrorMessage = "رقم الجوال مطلوب.")]
        [RegularExpression(
            SaudiMobileNumberValidator.RegularExpressionPattern,
            ErrorMessage = SaudiMobileNumberValidator.ErrorMessage
        )]
        public string PhoneNumber { get; set; } = string.Empty;

        [Display(Name = "كلمة المرور")]
        [Required(ErrorMessage = "كلمة المرور مطلوبة.")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "تأكيد كلمة المرور")]
        [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب.")]
        [Compare(nameof(Password), ErrorMessage = "تأكيد كلمة المرور غير مطابق.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "المسمى الوظيفي")]
        [Required(ErrorMessage = "المسمى الوظيفي مطلوب.")]
        public string JobTitle { get; set; } = string.Empty;

        [Display(Name = "اسم الجهة")]
        [Required(ErrorMessage = "اسم الجهة مطلوب.")]
        public string OrganizationName { get; set; } = string.Empty;

        [Display(Name = "هاتف الجهة")]
        [Required(ErrorMessage = "هاتف الجهة مطلوب.")]
        [RegularExpression(
            SaudiLandlineNumberValidator.RegularExpressionPattern,
            ErrorMessage = SaudiLandlineNumberValidator.ErrorMessage
        )]
        public string AdministrationPhone { get; set; } = string.Empty;

        [Display(Name = "البريد الإلكتروني")]
        [Required(ErrorMessage = "البريد الإلكتروني مطلوب.")]
        [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة.")]
        public string AdministrationEmail { get; set; } = string.Empty;

        [Display(Name = "العنوان")]
        [Required(ErrorMessage = "العنوان مطلوب.")]
        public string AdministrationAddress { get; set; } = string.Empty;

        public string? LogoPath { get; set; }

        public int CurrentStep { get; set; } = 1;
    }
}
