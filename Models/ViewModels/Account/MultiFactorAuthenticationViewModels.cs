using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.ViewModels.Account;

public sealed class TwoFactorAuthenticationViewModel
{
    [Required]
    public string ChallengeId { get; set; } = string.Empty;

    [Required(ErrorMessage = "أدخل رمز التحقق أو رمز الاسترداد.")]
    [Display(Name = "رمز التحقق")]
    public string Code { get; set; } = string.Empty;

    public bool RequiresEnrollment { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ManualKey { get; set; } = string.Empty;
    public string QrSvg { get; set; } = string.Empty;
}

public sealed class MfaRecoveryCodesViewModel
{
    public IReadOnlyList<string> RecoveryCodes { get; init; } = [];
    public string ContinueUrl { get; init; } = string.Empty;
}
