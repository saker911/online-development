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
    public static class PermitInputNormalizer
    {
        public static int NormalizePageSize(int pageSize)
        {
            return 5;
        }

        public static int ParsePermitNumber(string permitNumber)
        {
            if (string.IsNullOrWhiteSpace(permitNumber))
            {
                return 0;
            }

            var trimmed = permitNumber.Trim();
            if (
                trimmed.StartsWith("PERMIT-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(trimmed[7..], out var officialValue)
            )
            {
                return officialValue;
            }

            return trimmed.StartsWith("P", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(trimmed[1..], out var value)
                    ? value
                : int.TryParse(trimmed, out value) ? value
                : 0;
        }

        public static string NormalizeSearchTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return string.Empty;
            }

            var trimmed = term.Trim();
            if (
                trimmed.StartsWith("PERMIT-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(trimmed[7..], out var publicNumber)
            )
            {
                return $"PERMIT-{publicNumber:D5}";
            }

            if (
                trimmed.StartsWith("P", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(trimmed[1..], out var legacyNumber)
            )
            {
                return $"PERMIT-{legacyNumber:D5}";
            }

            return trimmed;
        }

        public static bool ContainsSearchValue(string? source, string normalizedTerm)
        {
            if (string.IsNullOrWhiteSpace(normalizedTerm))
            {
                return false;
            }

            return NormalizeSearchComparableValue(source)
                .Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeSearchComparableValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = string.Join(
                " ",
                value
                    .Trim()
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            );

            return NormalizeArabicDigits(normalized).ToUpperInvariant();
        }

        public static string NormalizePermitType(string? permitType)
        {
            if (
                string.Equals(
                    permitType,
                    Permit.PermitTypePermanent,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Permit.PermitTypePermanent;
            }

            if (
                string.Equals(permitType, Permit.PermitTypeExit, StringComparison.OrdinalIgnoreCase)
            )
            {
                return Permit.PermitTypeExit;
            }

            return Permit.PermitTypeVisitor;
        }

        public static string NormalizePlateNumber(string? plateNumber)
        {
            return NormalizeArabicDigits(
                (plateNumber ?? string.Empty)
                    .Replace(" ", string.Empty)
                    .Replace("-", string.Empty)
                    .Trim()
                    .ToUpperInvariant()
            );
        }

        public static string NormalizeArabicDigits(string value)
        {
            return value
                .Replace('٠', '0')
                .Replace('١', '1')
                .Replace('٢', '2')
                .Replace('٣', '3')
                .Replace('٤', '4')
                .Replace('٥', '5')
                .Replace('٦', '6')
                .Replace('٧', '7')
                .Replace('٨', '8')
                .Replace('٩', '9')
                .Replace('۰', '0')
                .Replace('۱', '1')
                .Replace('۲', '2')
                .Replace('۳', '3')
                .Replace('۴', '4')
                .Replace('۵', '5')
                .Replace('۶', '6')
                .Replace('۷', '7')
                .Replace('۸', '8')
                .Replace('۹', '9');
        }
    }
}
