using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Utilities.Security;

namespace VehiclePermitSystemWeb.Utilities.Permits
{
    internal static class PermitQrTokenGenerator
    {
        private const string CurrentVersionPrefix = "p1.";

        public static string Generate(Permit permit)
        {
            ArgumentNullException.ThrowIfNull(permit);
            return CurrentVersionPrefix + SecureTokenGenerator.GenerateSecureToken(32);
        }

        public static bool IsCurrentVersion(string? token)
        {
            return !string.IsNullOrWhiteSpace(token)
                && token.StartsWith(CurrentVersionPrefix, StringComparison.Ordinal);
        }
    }
}
