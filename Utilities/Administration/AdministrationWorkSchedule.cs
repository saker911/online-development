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

namespace VehiclePermitSystemWeb.Utilities.Administration
{
    public static class AdministrationWorkSchedule
    {
        private static readonly DayOfWeek[] DefaultOfficialWorkDays =
        {
            DayOfWeek.Sunday,
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
        };

        public static string DefaultOfficialWorkDaysCsv =>
            string.Join(',', DefaultOfficialWorkDays);

        public static string NormalizeOfficialWorkDaysCsv(string? csv)
        {
            return string.Join(',', ParseOfficialWorkDays(csv));
        }

        public static HashSet<DayOfWeek> ParseOfficialWorkDays(string? csv)
        {
            var officialWorkDays = new HashSet<DayOfWeek>();
            foreach (
                var segment in (csv ?? string.Empty).Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                if (Enum.TryParse<DayOfWeek>(segment.Trim(), ignoreCase: true, out var dayOfWeek))
                {
                    officialWorkDays.Add(dayOfWeek);
                }
            }

            return officialWorkDays;
        }

        public static bool IsOfficialWorkDay(DateTime referenceTime, string? officialWorkDaysCsv)
        {
            return ParseOfficialWorkDays(officialWorkDaysCsv).Contains(referenceTime.DayOfWeek);
        }

        public static bool IsWithinWorkHours(
            DateTime referenceTime,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string? officialWorkDaysCsv
        )
        {
            return TryGetShiftWindow(
                referenceTime,
                workStartTime,
                workEndTime,
                officialWorkDaysCsv,
                out _,
                out _
            );
        }

        public static bool IsWithinWorkHours(
            DateTime referenceTime,
            AdministrationSettings settings
        )
        {
            return IsWithinWorkHours(
                referenceTime,
                settings.WorkStartTime,
                settings.WorkEndTime,
                settings.OfficialWorkDaysCsv
            );
        }

        public static bool TryGetShiftWindow(
            DateTime referenceTime,
            AdministrationSettings settings,
            out DateTime shiftStart,
            out DateTime shiftEnd
        )
        {
            return TryGetShiftWindow(
                referenceTime,
                settings.WorkStartTime,
                settings.WorkEndTime,
                settings.OfficialWorkDaysCsv,
                out shiftStart,
                out shiftEnd
            );
        }

        public static bool TryGetShiftWindow(
            DateTime referenceTime,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string? officialWorkDaysCsv,
            out DateTime shiftStart,
            out DateTime shiftEnd
        )
        {
            shiftStart = default;
            shiftEnd = default;

            var officialWorkDays = ParseOfficialWorkDays(officialWorkDaysCsv);
            if (officialWorkDays.Count == 0)
            {
                return false;
            }

            var referenceDate = referenceTime.Date;
            var referenceClockTime = TimeOnly.FromDateTime(referenceTime);

            if (workStartTime <= workEndTime)
            {
                if (!officialWorkDays.Contains(referenceTime.DayOfWeek))
                {
                    return false;
                }

                if (referenceClockTime < workStartTime || referenceClockTime > workEndTime)
                {
                    return false;
                }

                shiftStart = referenceDate.Add(workStartTime.ToTimeSpan());
                shiftEnd = referenceDate.Add(workEndTime.ToTimeSpan());
                return true;
            }

            if (referenceClockTime >= workStartTime)
            {
                if (!officialWorkDays.Contains(referenceTime.DayOfWeek))
                {
                    return false;
                }

                shiftStart = referenceDate.Add(workStartTime.ToTimeSpan());
                shiftEnd = referenceDate.AddDays(1).Add(workEndTime.ToTimeSpan());
                return true;
            }

            if (referenceClockTime <= workEndTime)
            {
                var previousDate = referenceDate.AddDays(-1);
                if (!officialWorkDays.Contains(previousDate.DayOfWeek))
                {
                    return false;
                }

                shiftStart = previousDate.Add(workStartTime.ToTimeSpan());
                shiftEnd = referenceDate.Add(workEndTime.ToTimeSpan());
                return true;
            }

            return false;
        }

        public static DateTime GetExpectedShiftEnd(
            DateTime referenceTime,
            AdministrationSettings settings
        )
        {
            return GetExpectedShiftEnd(
                referenceTime,
                settings.WorkStartTime,
                settings.WorkEndTime,
                settings.OfficialWorkDaysCsv
            );
        }

        public static DateTime GetExpectedShiftEnd(
            DateTime referenceTime,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string? officialWorkDaysCsv
        )
        {
            if (
                TryGetShiftWindow(
                    referenceTime,
                    workStartTime,
                    workEndTime,
                    officialWorkDaysCsv,
                    out _,
                    out var activeShiftEnd
                )
            )
            {
                return activeShiftEnd;
            }

            var officialWorkDays = ParseOfficialWorkDays(officialWorkDaysCsv);
            if (officialWorkDays.Count == 0)
            {
                return referenceTime;
            }

            for (var offset = 0; offset <= 14; offset++)
            {
                var shiftStartDate = referenceTime.Date.AddDays(offset);
                if (!officialWorkDays.Contains(shiftStartDate.DayOfWeek))
                {
                    continue;
                }

                var shiftStart = shiftStartDate.Add(workStartTime.ToTimeSpan());
                var shiftEnd =
                    workStartTime <= workEndTime
                        ? shiftStartDate.Add(workEndTime.ToTimeSpan())
                        : shiftStartDate.AddDays(1).Add(workEndTime.ToTimeSpan());

                if (shiftEnd >= referenceTime && shiftStart <= shiftEnd)
                {
                    return shiftEnd;
                }
            }

            return referenceTime;
        }

        public static bool TryGetMostRecentWorkEnd(
            DateTime referenceTime,
            AdministrationSettings settings,
            out DateTime resolvedWorkEndTime
        )
        {
            return TryGetMostRecentWorkEnd(
                referenceTime,
                settings.WorkStartTime,
                settings.WorkEndTime,
                settings.OfficialWorkDaysCsv,
                out resolvedWorkEndTime
            );
        }

        public static bool TryGetMostRecentWorkEnd(
            DateTime referenceTime,
            TimeOnly workStartTime,
            TimeOnly workEndTime,
            string? officialWorkDaysCsv,
            out DateTime resolvedWorkEndTime
        )
        {
            resolvedWorkEndTime = default;

            var officialWorkDays = ParseOfficialWorkDays(officialWorkDaysCsv);
            if (officialWorkDays.Count == 0)
            {
                return false;
            }

            for (var offset = 0; offset <= 14; offset++)
            {
                var shiftStartDate = referenceTime.Date.AddDays(-offset);
                if (!officialWorkDays.Contains(shiftStartDate.DayOfWeek))
                {
                    continue;
                }

                var candidateWorkEndTime =
                    workStartTime <= workEndTime
                        ? shiftStartDate.Add(workEndTime.ToTimeSpan())
                        : shiftStartDate.AddDays(1).Add(workEndTime.ToTimeSpan());

                if (candidateWorkEndTime <= referenceTime)
                {
                    resolvedWorkEndTime = candidateWorkEndTime;
                    return true;
                }
            }

            return false;
        }
    }
}
