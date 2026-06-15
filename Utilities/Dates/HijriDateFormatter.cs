using System.Globalization;

namespace VehiclePermitSystemWeb.Utilities.Dates
{
    public static class HijriDateFormatter
    {
        private static readonly CultureInfo HijriCulture = BuildHijriCulture();
        private static readonly CultureInfo GregorianArabicCulture = BuildGregorianArabicCulture();
        private static readonly DateTime MinSupportedDate = new(1900, 4, 30);
        private static readonly DateTime MaxSupportedDate = new(2077, 11, 16, 23, 59, 59);

        public static string Format(DateTime? value, bool includeTime = true)
        {
            if (!value.HasValue)
            {
                return "-";
            }

            var localValue = NormalizeToLocal(value.Value);

            if (!IsSupported(localValue))
            {
                return FormatGregorian(localValue, includeTime);
            }

            var format = includeTime ? "dd MMMM yyyy هـ - hh:mm tt" : "dd MMMM yyyy هـ";
            return localValue.ToString(format, HijriCulture);
        }

        public static string FormatWithDay(DateTime? value, bool includeTime = true)
        {
            if (!value.HasValue)
            {
                return "-";
            }

            var localValue = NormalizeToLocal(value.Value);

            if (!IsSupported(localValue))
            {
                return FormatGregorian(localValue, includeTime, includeDay: true);
            }

            var format = includeTime ? "dddd dd MMMM yyyy هـ - hh:mm tt" : "dddd dd MMMM yyyy هـ";
            return localValue.ToString(format, HijriCulture);
        }

        public static bool IsSupported(DateTime value)
        {
            return value >= MinSupportedDate && value <= MaxSupportedDate;
        }

        private static string FormatGregorian(
            DateTime value,
            bool includeTime,
            bool includeDay = false
        )
        {
            var format = includeDay
                ? includeTime
                    ? "dddd yyyy/MM/dd - hh:mm tt"
                    : "dddd yyyy/MM/dd"
                : includeTime
                    ? "yyyy/MM/dd - hh:mm tt"
                    : "yyyy/MM/dd";

            return value.ToString(format, GregorianArabicCulture);
        }

        private static DateTime NormalizeToLocal(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value.ToLocalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local),
                _ => value,
            };
        }

        private static CultureInfo BuildHijriCulture()
        {
            var culture = new CultureInfo("ar-SA");
            var dateTimeFormat = (DateTimeFormatInfo)culture.DateTimeFormat.Clone();
            dateTimeFormat.Calendar = new UmAlQuraCalendar();
            dateTimeFormat.AMDesignator = "ص";
            dateTimeFormat.PMDesignator = "م";
            culture.DateTimeFormat = dateTimeFormat;
            return culture;
        }

        private static CultureInfo BuildGregorianArabicCulture()
        {
            var culture = new CultureInfo("ar-SA");
            var dateTimeFormat = (DateTimeFormatInfo)culture.DateTimeFormat.Clone();
            dateTimeFormat.Calendar = new GregorianCalendar();
            dateTimeFormat.AMDesignator = "ص";
            dateTimeFormat.PMDesignator = "م";
            culture.DateTimeFormat = dateTimeFormat;
            return culture;
        }
    }
}
