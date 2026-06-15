using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VehiclePermitSystemWeb.Models.DTOs;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Account;
using VehiclePermitSystemWeb.Models.ViewModels.Backup;
using VehiclePermitSystemWeb.Models.ViewModels.Delegations;
using VehiclePermitSystemWeb.Models.ViewModels.Departments;
using VehiclePermitSystemWeb.Models.ViewModels.Display;
using VehiclePermitSystemWeb.Models.ViewModels.Permits;
using VehiclePermitSystemWeb.Models.ViewModels.Reports;
using VehiclePermitSystemWeb.Models.ViewModels.Scan;
using VehiclePermitSystemWeb.Models.ViewModels.Users;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;

namespace VehiclePermitSystemWeb.Utilities.Permits
{
    internal static class PermitQrTokenGenerator
    {
        public static string Generate(Permit permit)
        {
            var exp = permit.ExpiresAt.HasValue
                ? new DateTimeOffset(permit.ExpiresAt.Value).ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds();

            var payloadObject = new
            {
                type = "permit",
                id = permit.PublicPermitCode,
                exp,
            };
            var payloadJson = JsonSerializer.Serialize(payloadObject);
            var signature = ComputeHmac(payloadJson, permit.PublicPermitCode);
            var tokenObject = new { payload = payloadObject, sig = signature };
            var tokenJson = JsonSerializer.Serialize(tokenObject);
            return Base64UrlEncode(tokenJson);
        }

        private static string ComputeHmac(string text, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(text));
            return Convert.ToBase64String(hash);
        }

        private static string Base64UrlEncode(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            return Convert
                .ToBase64String(bytes)
                .Replace("=", string.Empty)
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
