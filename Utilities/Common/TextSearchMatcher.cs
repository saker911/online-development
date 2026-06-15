namespace VehiclePermitSystemWeb.Utilities.Common
{
    public static class TextSearchMatcher
    {
        public static bool ContainsValue(string? value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }
}
