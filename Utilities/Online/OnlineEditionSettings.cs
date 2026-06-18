using System.Security.Cryptography;
using System.Text;

namespace VehiclePermitSystemWeb.Utilities.Online;

public static class OnlineEditionSettings
{
    private const string SectionPrefix = "OnlineEdition";

    public static bool PrivacyMinimized(IConfiguration configuration)
    {
        return configuration.GetValue<bool>($"{SectionPrefix}:PrivacyMinimized");
    }

    public static bool CollectNationalId(IConfiguration configuration)
    {
        return configuration.GetValue($"{SectionPrefix}:CollectNationalId", true);
    }

    public static bool HideSensitiveIdentityFields(IConfiguration configuration)
    {
        return PrivacyMinimized(configuration) && !CollectNationalId(configuration);
    }

    public static bool SimplifiedVisits(IConfiguration configuration)
    {
        return configuration.GetValue($"{SectionPrefix}:SimplifiedVisits", false);
    }

    public static string IdentityDisplayLabel(IConfiguration configuration)
    {
        return HideSensitiveIdentityFields(configuration) ? "المعرف الداخلي" : "رقم الهوية";
    }

    public static string IdentitySearchLabel(IConfiguration configuration)
    {
        return HideSensitiveIdentityFields(configuration) ? "المعرف الداخلي" : "الهوية";
    }

    public static string BuildSyntheticNationalId(params string?[] seedParts)
    {
        var seed = string.Join("|", seedParts.Select(part => part?.Trim() ?? string.Empty));
        if (string.IsNullOrWhiteSpace(seed))
        {
            seed = Guid.NewGuid().ToString("N");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var value = BitConverter.ToUInt32(hash, 0) % 1_000_000_000;
        return $"2{value:D9}";
    }
}
