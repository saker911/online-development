namespace VehiclePermitSystemWeb.Utilities.Security
{
    public static class QrSecretStore
    {
        public static string LoadOrCreate(string secretPath)
        {
            try
            {
                if (File.Exists(secretPath))
                {
                    var existing = File.ReadAllText(secretPath).Trim();
                    if (!string.IsNullOrWhiteSpace(existing))
                    {
                        return existing;
                    }
                }

                var secret = SecureTokenGenerator.GenerateSecureToken();
                File.WriteAllText(secretPath, secret);
                return secret;
            }
            catch
            {
                return SecureTokenGenerator.GenerateSecureToken();
            }
        }
    }
}
