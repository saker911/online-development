using System;
using System.Linq;

namespace VehiclePermitSystemWeb.Utilities.Security
{
    public static class PasswordValidationRules
    {
        public static bool IsStrongPassword(string? password)
        {
            var normalizedPassword = password ?? string.Empty;
            return normalizedPassword.Length >= 8
                && normalizedPassword.Any(char.IsUpper)
                && normalizedPassword.Any(char.IsLower)
                && normalizedPassword.Any(char.IsDigit)
                && normalizedPassword.Any(character => !char.IsLetterOrDigit(character));
        }
    }
}
