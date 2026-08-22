using System.Text.RegularExpressions;

namespace VehiclePermitSystemWeb.Utilities.Security;

public static partial class AccountUsernameValidator
{
    public const string RegularExpressionPattern = @"^[A-Za-z0-9][A-Za-z0-9._-]{3,63}$";
    public const string ErrorMessage =
        "معرف الحساب يجب أن يتكون من 4 إلى 64 حرفًا أو رقمًا، ويمكن استخدام النقطة والشرطة والشرطة السفلية.";

    public static bool IsValid(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return UsernameRegex().IsMatch(normalized);
    }

    [GeneratedRegex(RegularExpressionPattern, RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();
}
