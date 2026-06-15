using System.Net;
using Microsoft.AspNetCore.Http;

namespace VehiclePermitSystemWeb.Utilities.Common
{
    public static class ClientIpRangeMatcher
    {
        public static List<string> ParseAllowedClientIpRanges(string? allowedClientIpRanges)
        {
            return (allowedClientIpRanges ?? string.Empty)
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Trim())
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .ToList();
        }

        public static IPAddress? ResolveClientIpAddress(HttpContext context)
        {
            return context.Connection.RemoteIpAddress;
        }

        public static bool IsClientIpInRange(IPAddress clientIp, string allowedRange)
        {
            if (string.IsNullOrWhiteSpace(allowedRange))
            {
                return false;
            }

            if (allowedRange == "*")
            {
                return true;
            }

            if (allowedRange.Contains('/') && TryMatchCidrRange(clientIp, allowedRange))
            {
                return true;
            }

            if (IPAddress.TryParse(allowedRange, out var exactAddress))
            {
                return string.Equals(
                    clientIp.ToString(),
                    exactAddress.ToString(),
                    StringComparison.OrdinalIgnoreCase
                );
            }

            if (clientIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return MatchWildcardIpv4(clientIp, allowedRange);
            }

            return false;
        }

        private static bool MatchWildcardIpv4(IPAddress clientIp, string allowedRange)
        {
            var clientParts = clientIp.ToString().Split('.');
            var rangeParts = allowedRange.Split('.');

            if (rangeParts.Length == 3)
            {
                var shorthandPart = rangeParts[2].Trim();
                var wildcardIndex = shorthandPart.IndexOfAny(new[] { 'x', 'X', '*' });
                if (wildcardIndex >= 0)
                {
                    return clientParts.Length == 4
                        && string.Equals(
                            clientParts[0],
                            rangeParts[0].Trim(),
                            StringComparison.Ordinal
                        )
                        && string.Equals(
                            clientParts[1],
                            rangeParts[1].Trim(),
                            StringComparison.Ordinal
                        )
                        && clientParts[2]
                            .StartsWith(shorthandPart[..wildcardIndex], StringComparison.Ordinal);
                }
            }

            if (rangeParts.Length != clientParts.Length)
            {
                return false;
            }

            for (var index = 0; index < rangeParts.Length; index++)
            {
                var rangePart = rangeParts[index].Trim();
                if (string.IsNullOrWhiteSpace(rangePart))
                {
                    return false;
                }

                if (rangePart is "x" or "X" or "*")
                {
                    return true;
                }

                var wildcardIndex = rangePart.IndexOfAny(new[] { 'x', 'X', '*' });
                if (wildcardIndex >= 0)
                {
                    var prefix = rangePart[..wildcardIndex];
                    return clientParts[index].StartsWith(prefix, StringComparison.Ordinal);
                }

                if (!string.Equals(clientParts[index], rangePart, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryMatchCidrRange(IPAddress clientIp, string allowedRange)
        {
            var parts = allowedRange.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (parts.Length != 2)
            {
                return false;
            }

            if (clientIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return false;
            }

            if (!IPAddress.TryParse(parts[0], out var networkAddress))
            {
                return false;
            }

            if (
                !int.TryParse(parts[1], out var prefixLength)
                || prefixLength < 0
                || prefixLength > 32
            )
            {
                return false;
            }

            var clientBytes = clientIp.GetAddressBytes();
            var networkBytes = networkAddress.GetAddressBytes();
            if (clientBytes.Length != 4 || networkBytes.Length != 4)
            {
                return false;
            }

            var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
            var clientValue = BitConverter.ToUInt32(clientBytes.Reverse().ToArray(), 0);
            var networkValue = BitConverter.ToUInt32(networkBytes.Reverse().ToArray(), 0);
            return (clientValue & mask) == (networkValue & mask);
        }
    }
}
