using System.Security.Cryptography;

namespace VehiclePermitSystemWeb.Utilities.Security
{
    public static class SecureTokenGenerator
    {
        public static string GenerateSecureToken(int byteCount = 32)
        {
            var bytes = RandomNumberGenerator.GetBytes(byteCount);
            return Convert
                .ToBase64String(bytes)
                .Replace("=", string.Empty)
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
