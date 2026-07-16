namespace VehiclePermitSystemWeb.Security
{
    public static class ExternalAuthenticationDefaults
    {
        public const string CookieScheme = "ExternalIdentity";
        public const string GoogleScheme = "Google";
        public const string MicrosoftScheme = "Microsoft";

        public static string? NormalizeProvider(string? provider)
        {
            return provider?.Trim().ToLowerInvariant() switch
            {
                "google" => GoogleScheme,
                "microsoft" or "hotmail" or "outlook" => MicrosoftScheme,
                _ => null,
            };
        }
    }
}
