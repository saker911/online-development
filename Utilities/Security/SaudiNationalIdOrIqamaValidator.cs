using System.Linq;

namespace VehiclePermitSystemWeb.Utilities.Security
{
    public static class SaudiNationalIdOrIqamaValidator
    {
        public const string RegularExpressionPattern = @"^[12]\d{9}$";
        public const string ErrorMessage =
            "رقم الهوية/الإقامة يجب أن يكون 10 أرقام ويبدأ بـ 1 أو 2.";

        public static bool IsValid(string? value)
        {
            var normalizedValue = (value ?? string.Empty).Trim();
            return normalizedValue.Length == 10
                && normalizedValue.All(char.IsDigit)
                && (normalizedValue.StartsWith("1") || normalizedValue.StartsWith("2"));
        }
    }
}
