using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VehiclePermitSystemWeb.Utilities.Security;

public static partial class PersonalDataSanitizer
{
    private static readonly string[] SensitivePropertyFragments =
    [
        "nationalid",
        "civilid",
        "iqama",
        "phonenumber",
        "employeephone",
        "visitoremail",
        "email",
        "password",
        "signature",
        "photo",
        "token",
        "secret",
    ];

    public static string CreateInternalReference()
    {
        return $"REF-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
    }

    public static bool IsInternalReference(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.StartsWith("REF-", StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizeAuditJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                return string.Empty;
            }

            RedactNode(node);
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    public static string SanitizeAuditText(string? value)
    {
        var sanitized = value ?? string.Empty;
        sanitized = NationalIdentityRegex().Replace(sanitized, "[معرف محجوب]");
        sanitized = SaudiMobileRegex().Replace(sanitized, "[جوال محجوب]");
        sanitized = EmailRegex().Replace(sanitized, "[بريد محجوب]");
        return sanitized;
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (IsSensitiveProperty(property.Key))
                {
                    obj[property.Key] = "[محجوب]";
                }
                else if (property.Value != null)
                {
                    RedactNode(property.Value);
                }
            }

            return;
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item != null)
                {
                    RedactNode(item);
                }
            }
        }
    }

    private static bool IsSensitiveProperty(string name)
    {
        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return SensitivePropertyFragments.Any(normalized.Contains);
    }

    [GeneratedRegex(@"(?<!\d)[12]\d{9}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdentityRegex();

    [GeneratedRegex(@"(?<!\d)05\d{8}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SaudiMobileRegex();

    [GeneratedRegex(@"\b[^\s@]+@[^\s@]+\.[^\s@]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
