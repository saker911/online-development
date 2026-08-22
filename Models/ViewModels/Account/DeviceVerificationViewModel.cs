using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Account
{
    public sealed class DeviceVerificationViewModel
    {
        public string ChallengeId { get; set; } = string.Empty;
        public string MaskedEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "أدخل رمز التحقق المرسل إلى بريدك.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "رمز التحقق مكوّن من 6 أرقام.")]
        public string Code { get; set; } = string.Empty;
    }
}
