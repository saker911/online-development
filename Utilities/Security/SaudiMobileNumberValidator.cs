using System.Linq;

namespace VehiclePermitSystemWeb.Utilities.Security
{
    public static class SaudiMobileNumberValidator
    {
        public const string RegularExpressionPattern = @"^05\d{8}$";
        public const string OptionalRegularExpressionPattern = @"^$|^05\d{8}$";
        public const string ErrorMessage = "رقم الجوال يجب أن يكون 10 أرقام ويبدأ بـ 05.";
        public const string OptionalErrorMessage =
            "رقم الجوال يجب أن يكون 10 أرقام ويبدأ بـ 05 عند إدخاله.";

        public static bool IsValidRequired(string? value)
        {
            var normalizedValue = (value ?? string.Empty).Trim();
            return normalizedValue.Length == 10
                && normalizedValue.All(char.IsDigit)
                && normalizedValue.StartsWith("05");
        }

        public static bool IsValidOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) || IsValidRequired(value);
        }
    }
}
