using System.Globalization;

namespace VehiclePermitSystemWeb.Utilities.Dates
{
    public static class DateHelper
    {
        public static string ToArabicDate(DateTime date)
        {
            var localDate = NormalizeToLocal(date);

            return localDate
                .ToString("yyyy/MM/dd hh:mm tt", new CultureInfo("ar-SA"))
                .Replace("AM", "ص")
                .Replace("PM", "م");
        }

        public static string ToArabicHijri(DateTime date)
        {
            var culture = new CultureInfo("ar-SA");
            culture.DateTimeFormat.Calendar = new HijriCalendar();
            var localDate = NormalizeToLocal(date);

            return localDate
                .ToString("dd MMMM yyyy hh:mm tt", culture)
                .Replace("AM", "ص")
                .Replace("PM", "م");
        }

        public static string ToGregorianDateTime12(DateTime date)
        {
            var localDate = NormalizeToLocal(date);
            return $"{localDate.ToString("yyyy-MM-dd hh:mm", CultureInfo.InvariantCulture)} {GetArabicPeriod(localDate)}";
        }

        public static string ToGregorianClock12(DateTime date)
        {
            var localDate = NormalizeToLocal(date);
            return $"{localDate.ToString("hh:mm", CultureInfo.InvariantCulture)} {GetArabicPeriod(localDate)}";
        }

        private static string GetArabicPeriod(DateTime date) => date.Hour < 12 ? "ص" : "م";

        private static DateTime NormalizeToLocal(DateTime date)
        {
            return date.Kind switch
            {
                DateTimeKind.Utc => date.ToLocalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(date, DateTimeKind.Local),
                _ => date,
            };
        }
    }
}
