namespace VehiclePermitSystemWeb.Security
{
    public static class ExternalAuthenticationDefaults
    {
        public const string CookieScheme = "ExternalIdentity";
        public const string GoogleScheme = "Google";

        public static string? NormalizeProvider(string? provider)
        {
            return provider?.Trim().ToLowerInvariant() switch
            {
                "google" => GoogleScheme,
                _ => null,
            };
        }
    }
}
