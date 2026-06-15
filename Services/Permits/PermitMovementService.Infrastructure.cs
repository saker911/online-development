using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VehiclePermitSystemWeb.Data;
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

namespace VehiclePermitSystemWeb.Services.Permits
{
    public sealed partial class PermitMovementService
    {
        private static PermitScanAuditContext BuildAuditContext(
            PermitScanAuditContext? auditContext,
            string source,
            string? sequenceId,
            string classificationStatus
        )
        {
            return PermitAuditContextBuilder.BuildAuditContext(
                auditContext,
                source,
                sequenceId,
                classificationStatus
            );
        }

        private static string NormalizeArabicDigits(string value)
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
                .Replace('٩', '9');
        }

        private static string NormalizePermitIdentifier(string? permitNumber)
        {
            if (string.IsNullOrWhiteSpace(permitNumber))
            {
                return string.Empty;
            }

            if (TryGetPermitSequenceNumber(permitNumber, out var sequenceNumber))
            {
                return $"PERMIT-{sequenceNumber:D5}";
            }

            return permitNumber.Trim();
        }

        private static bool TryGetPermitSequenceNumber(string? permitNumber, out int sequenceNumber)
        {
            sequenceNumber = 0;
            if (string.IsNullOrWhiteSpace(permitNumber))
            {
                return false;
            }

            var normalized = NormalizeArabicDigits(permitNumber.Trim());
            var digits = new string(normalized.Where(char.IsDigit).ToArray());
            return digits.Length > 0 && int.TryParse(digits, out sequenceNumber);
        }

        private static string[] BuildPermitLookupKeys(string? permitNumber)
        {
            if (string.IsNullOrWhiteSpace(permitNumber))
            {
                return Array.Empty<string>();
            }

            var normalized = NormalizePermitIdentifier(permitNumber);
            var keys = new List<string> { normalized };

            if (TryGetPermitSequenceNumber(permitNumber, out var sequenceNumber))
            {
                keys.Add($"PERMIT-{sequenceNumber:D5}");
                keys.Add($"P{sequenceNumber}");
                keys.Add($"P{sequenceNumber:D4}");
                keys.Add($"P{sequenceNumber:D5}");
                keys.Add($"P{sequenceNumber:D6}");
            }

            return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string NormalizePermitOperationType(string actionType)
        {
            return actionType switch
            {
                "LateReturn" => "Entry",
                "LateAttendance" => "Entry",
                "Return" => "Entry",
                "ReturnAfterUnauthorizedExit" => "Entry",
                "WorkEndEntry" => "Entry",
                "Entry" => "Entry",
                "ExitAuthorized" => "Exit",
                "ExitUnauthorized" => "Exit",
                "ExitFinal" => "Exit",
                "WorkEndExit" => "Exit",
                "LateCheckout" => "Exit",
                "NoReturnWarning" => "Warning",
                "SecurityViolation" => "Violation",
                "NoReturnViolation" => "Violation",
                "DuplicateIgnored" => "Duplicate",
                _ => actionType,
            };
        }

        private static bool IsDuplicateProtectedActivity(string actionType)
        {
            var normalized = NormalizePermitOperationType(actionType);
            return string.Equals(normalized, "Entry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Duplicate", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPermitActivitySnapshotCacheKey(string permitNumber)
        {
            return $"permit:last:{NormalizePermitIdentifier(permitNumber).ToUpperInvariant()}";
        }

        private static string GetDuplicateActivityCacheKey(
            string permitNumber,
            string operationType
        )
        {
            return $"permit:duplicate:{NormalizePermitIdentifier(permitNumber).ToUpperInvariant()}:{NormalizePermitOperationType(operationType)}";
        }

        private bool TryGetCachedPermitActivitySnapshot(
            string permitNumber,
            out PermitActivitySnapshot? snapshot
        )
        {
            return _memoryCache.TryGetValue(
                GetPermitActivitySnapshotCacheKey(permitNumber),
                out snapshot
            );
        }

        private void CachePermitActivitySnapshot(
            string permitNumber,
            string actionType,
            DateTime occurredAt
        )
        {
            _memoryCache.Set(
                GetPermitActivitySnapshotCacheKey(permitNumber),
                new PermitActivitySnapshot(actionType, occurredAt),
                ShortCacheWindow
            );
            _memoryCache.Set(
                GetDuplicateActivityCacheKey(permitNumber, actionType),
                occurredAt,
                ShortCacheWindow
            );
        }

        private static string GetDuplicateScanMessage(
            Permit permit,
            PermitActivitySnapshot? latestActivity
        )
        {
            if (latestActivity == null)
            {
                return "DuplicateIgnored";
            }

            var latestOperation = NormalizePermitOperationType(latestActivity.ActionType);
            var isEntrySide =
                string.Equals(latestOperation, "Entry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    latestActivity.ActionType,
                    "WorkEndEntry",
                    StringComparison.OrdinalIgnoreCase
                );

            if (permit.CurrentState == PermitCurrentStateInside || isEntrySide)
            {
                return "AlreadyEntered";
            }

            if (
                string.Equals(latestOperation, "Exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    latestActivity.ActionType,
                    "WorkEndExit",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "AlreadyExited";
            }

            return "DuplicateIgnored";
        }

        private static int GetLateMinutes(TimeSpan delay)
        {
            return Math.Max(1, (int)Math.Ceiling(delay.TotalMinutes));
        }

        private WorkHoursSettings GetWorkHoursSettings()
        {
            var settings = GetAdministrationSettings();
            return new WorkHoursSettings(
                settings.WorkStartTime,
                settings.WorkEndTime,
                TimeSpan.FromMinutes(Math.Max(0, settings.AttendanceGraceMinutes)),
                TimeSpan.FromMinutes(Math.Max(0, settings.WorkEndExitGraceMinutes)),
                TimeSpan.FromMinutes(Math.Max(0, settings.LateReturnGraceMinutes)),
                settings.OfficialWorkDaysCsv
            );
        }

        private static string DescribeDelay(TimeSpan delay)
        {
            var totalMinutes = Math.Max(1, (int)Math.Ceiling(delay.TotalMinutes));
            if (totalMinutes < 60)
            {
                return $"{totalMinutes} دقيقة";
            }

            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return minutes == 0 ? $"{hours} ساعة" : $"{hours} ساعة و{minutes} دقيقة";
        }

        private AdministrationSettings GetAdministrationSettings()
        {
            using var db = _dbContextFactory.CreateDbContext();
            return GetAdministrationSettings(db);
        }

        private static AdministrationSettings GetAdministrationSettings(ApplicationDbContext db)
        {
            var existing = db.AdministrationSettings.SingleOrDefault(x => x.Id == 1);
            if (existing != null)
            {
                return existing;
            }

            existing = new AdministrationSettings
            {
                Id = 1,
                WorkStartTime = new TimeOnly(8, 0),
                WorkEndTime = new TimeOnly(16, 0),
                AttendanceGraceMinutes = 15,
                WorkEndExitGraceMinutes = 30,
                LateReturnGraceMinutes = 5,
                OfficialWorkDaysCsv = AdministrationWorkSchedule.DefaultOfficialWorkDaysCsv,
            };

            db.AdministrationSettings.Add(existing);
            db.SaveChanges();
            return existing;
        }

        private static bool ShouldExpireAfterReturn(Permit permit)
        {
            return permit.RequiresReturn
                && !string.Equals(
                    permit.PermitType,
                    Permit.PermitTypePermanent,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool IsPermitActiveForScan(Permit permit)
        {
            return !string.Equals(
                    permit.ApprovalStatus,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase
                )
                && !string.Equals(
                    permit.ApprovalStatus,
                    "Rejected",
                    StringComparison.OrdinalIgnoreCase
                )
                && !string.Equals(
                    permit.ApprovalStatus,
                    "Stopped",
                    StringComparison.OrdinalIgnoreCase
                )
                && !string.Equals(
                    permit.ApprovalStatus,
                    "Expired",
                    StringComparison.OrdinalIgnoreCase
                )
                && !string.Equals(
                    permit.ApprovalStatus,
                    "Exited",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static DateTime CalculateVisitorPermitExpiration(
            DateTime referenceTime,
            WorkHoursSettings workHours
        )
        {
            return AdministrationWorkSchedule.GetExpectedShiftEnd(
                referenceTime,
                workHours.StartTime,
                workHours.EndTime,
                workHours.OfficialWorkDaysCsv
            );
        }

        private static bool EnsureVisitorPermitExpiration(
            Permit permit,
            DateTime referenceTime,
            WorkHoursSettings workHours
        )
        {
            if (!permit.IsVisitorPermit || permit.ExpiresAt.HasValue)
            {
                return false;
            }

            permit.ExpiresAt = CalculateVisitorPermitExpiration(referenceTime, workHours);
            return true;
        }

    }
}
