using System.Linq;

namespace VehiclePermitSystemWeb.Utilities.Security
{
    public static class SaudiContactNumberValidator
    {
        public const string RegularExpressionPattern = @"^(?:05|01)\d{8}$";
        public const string OptionalRegularExpressionPattern = @"^$|^(?:05|01)\d{8}$";
        public const string ErrorMessage =
            "رقم التواصل يجب أن يبدأ بـ 05 أو 01 ويتكون من 10 أرقام.";
        public const string OptionalErrorMessage =
            "رقم التواصل يجب أن يبدأ بـ 05 أو 01 ويتكون من 10 أرقام عند إدخاله.";

        public static bool IsValidRequired(string? value)
        {
            var normalizedValue = (value ?? string.Empty).Trim();
            return normalizedValue.Length == 10
                && normalizedValue.All(char.IsDigit)
                && (normalizedValue.StartsWith("05") || normalizedValue.StartsWith("01"));
        }

        public static bool IsValidOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) || IsValidRequired(value);
        }
    }
}
