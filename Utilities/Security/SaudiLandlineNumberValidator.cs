using System.Linq;

namespace VehiclePermitSystemWeb.Utilities.Security
{
    public static class SaudiLandlineNumberValidator
    {
        public const string RegularExpressionPattern = @"^01\d{8}$";
        public const string OptionalRegularExpressionPattern = @"^$|^01\d{8}$";
        public const string ErrorMessage = "رقم الهاتف الثابت يجب أن يكون 10 أرقام ويبدأ بـ 01.";
        public const string OptionalErrorMessage =
            "رقم الهاتف الثابت يجب أن يكون 10 أرقام ويبدأ بـ 01 عند إدخاله.";

        public static bool IsValidRequired(string? value)
        {
            var normalizedValue = (value ?? string.Empty).Trim();
            return normalizedValue.Length == 10
                && normalizedValue.All(char.IsDigit)
                && normalizedValue.StartsWith("01");
        }

        public static bool IsValidOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) || IsValidRequired(value);
        }
    }
}
