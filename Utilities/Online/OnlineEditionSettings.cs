using VehiclePermitSystemWeb.Utilities.Security;

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

    public static int CompletedVisitPersonalDataRetentionDays(IConfiguration configuration)
    {
        return Math.Clamp(
            configuration.GetValue($"{SectionPrefix}:CompletedVisitPersonalDataRetentionDays", 180),
            30,
            2555
        );
    }

    public static int ArchivedPermitPersonalDataRetentionDays(IConfiguration configuration)
    {
        return Math.Clamp(
            configuration.GetValue($"{SectionPrefix}:ArchivedPermitPersonalDataRetentionDays", 365),
            30,
            2555
        );
    }

    public static int InactivePersonPhotoRetentionDays(IConfiguration configuration)
    {
        return Math.Clamp(
            configuration.GetValue($"{SectionPrefix}:InactivePersonPhotoRetentionDays", 30),
            7,
            365
        );
    }

    public static int SecurityIpRetentionDays(IConfiguration configuration)
    {
        return Math.Clamp(
            configuration.GetValue($"{SectionPrefix}:SecurityIpRetentionDays", 90),
            7,
            365
        );
    }

    public static string IdentityDisplayLabel(IConfiguration configuration)
    {
        return HideSensitiveIdentityFields(configuration) ? "المعرف الداخلي" : "رقم الهوية";
    }

    public static string IdentitySearchLabel(IConfiguration configuration)
    {
        return HideSensitiveIdentityFields(configuration) ? "المعرف الداخلي" : "الهوية";
    }

    public static string BuildInternalReference()
    {
        return PersonalDataSanitizer.CreateInternalReference();
    }
}
