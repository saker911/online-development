namespace VehiclePermitSystemWeb.Utilities.Permits
{
    public static class PermitVerificationUrlParser
    {
        public static bool TryGetToken(string? value, out string token)
        {
            token = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalizedValue = value.Trim();
            if (
                !Uri.TryCreate(normalizedValue, UriKind.Absolute, out var uri)
                || !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        uri.Scheme,
                        Uri.UriSchemeHttps,
                        StringComparison.OrdinalIgnoreCase
                    ))
            )
            {
                var relativePath = normalizedValue.StartsWith('/')
                    ? normalizedValue
                    : $"/{normalizedValue}";
                uri = new Uri(new Uri("https://permit.local"), relativePath);
            }

            var queryToken = uri
                .Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .FirstOrDefault(part =>
                    part.Length == 2
                    && string.Equals(part[0], "token", StringComparison.OrdinalIgnoreCase)
                )
                ?[1];
            if (!string.IsNullOrWhiteSpace(queryToken))
            {
                token = Uri.UnescapeDataString(queryToken.Replace("+", " "));
                return !string.IsNullOrWhiteSpace(token);
            }

            var pathSegments = uri
                .AbsolutePath.Trim('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            var permitSegmentIndex = Array.FindIndex(
                pathSegments,
                segment => string.Equals(segment, "permit", StringComparison.OrdinalIgnoreCase)
            );
            if (permitSegmentIndex < 0 || permitSegmentIndex >= pathSegments.Length - 1)
            {
                return false;
            }

            token = Uri.UnescapeDataString(pathSegments[permitSegmentIndex + 1]);
            return !string.IsNullOrWhiteSpace(token);
        }
    }
}
