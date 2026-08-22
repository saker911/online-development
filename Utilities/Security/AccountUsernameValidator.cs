using System.Text.RegularExpressions;

namespace VehiclePermitSystemWeb.Utilities.Security;

public static partial class AccountUsernameValidator
{
    public const string RegularExpressionPattern = @"^[A-Za-z0-9][A-Za-z0-9._-]{3,63}$";
    public const string ErrorMessage =
        "اسم المستخدم يجب أن يتكون من 4 إلى 64 خانة دون مسافات.";

    public static bool IsValid(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return UsernameRegex().IsMatch(normalized);
    }

    [GeneratedRegex(RegularExpressionPattern, RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();
}

public static partial class NewAccountUsernameValidator
{
    public const string RegularExpressionPattern =
        @"^[A-Za-z](?:[A-Za-z0-9._-]{2,62}[A-Za-z0-9])$";
    public const string ErrorMessage =
        "اسم المستخدم يجب أن يكون من 4 إلى 64 خانة، يبدأ بحرف إنجليزي، وينتهي بحرف أو رقم.";

    public static bool IsValid(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return UsernameRegex().IsMatch(normalized);
    }

    [GeneratedRegex(RegularExpressionPattern, RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();
}
