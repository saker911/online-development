using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Monitoring;
using VehiclePermitSystemWeb.Services.Common;

namespace VehiclePermitSystemWeb.Services.Monitoring
{
    public sealed class MonitoringDashboardService : IMonitoringDashboardService
    {
        private const int MaxRecentActivities = 8;
        private const int MaxSummaryItems = 5;
        private const int MaxSystemErrors = 5;
        private const int MaxLogsLoaded = 1000;

        private static readonly string[] EntryActionTypes =
        {
            "Entry",
            "LateReturn",
            "Return",
            "ReturnAfterUnauthorizedExit",
            "WorkEndEntry",
        };

        private static readonly string[] ExitActionTypes =
        {
            "ExitAuthorized",
            "ExitUnauthorized",
            "ExitFinal",
            "WorkEndExit",
        };

        private static readonly string[] ScanActionTypes =
        {
            "PermitScanSuccess",
            "PermitScanDenied",
            "VisitScanSuccess",
            "VisitScanDenied",
            "PendingUnauthorizedExit",
        };

        private static readonly string[] DeniedActionTypes =
        {
            "PermitScanDenied",
            "VisitScanDenied",
            "PendingUnauthorizedExit",
        };

        private static readonly string[] SystemErrorActionTypes =
        {
            "SystemError",
            "UnhandledException",
        };

        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;

        public MonitoringDashboardService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
        }

        public MonitoringDashboardViewModel BuildDashboard(string? range)
        {
            var selectedRange = NormalizeRange(range);
            var periodEnd = _systemClock.LocalNow;
            var periodStart = GetPeriodStart(selectedRange, periodEnd);

            using var db = _dbContextFactory.CreateDbContext();
            var logs = db
                .AuditLogs.AsNoTracking()
                .Where(log => log.OccurredAt >= periodStart && log.OccurredAt <= periodEnd)
                .OrderByDescending(log => log.OccurredAt)
                .Take(MaxLogsLoaded)
                .ToList();

            return new MonitoringDashboardViewModel
            {
                Range = selectedRange,
                RangeLabel = GetRangeLabel(selectedRange),
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                EntryCount = logs.Count(log => EntryActionTypes.Contains(log.ActionType)),
                ExitCount = logs.Count(log => ExitActionTypes.Contains(log.ActionType)),
                DeniedCount = logs.Count(log =>
                    !log.Success && DeniedActionTypes.Contains(log.ActionType)
                ),
                RecentActivities = BuildRecentActivities(logs),
                TopOperators = BuildTopOperators(logs),
                TopRejectionReasons = BuildTopRejectionReasons(logs),
                RecentSystemErrors = BuildRecentSystemErrors(logs),
            };
        }

        private static IReadOnlyList<MonitoringActivityItem> BuildRecentActivities(
            IReadOnlyList<AuditLog> logs
        )
        {
            return logs.Where(log =>
                    EntryActionTypes.Contains(log.ActionType)
                    || ExitActionTypes.Contains(log.ActionType)
                    || ScanActionTypes.Contains(log.ActionType)
                    || SystemErrorActionTypes.Contains(log.ActionType)
                )
                .Take(MaxRecentActivities)
                .Select(log => new MonitoringActivityItem
                {
                    Key = log.EntityId,
                    ActionType = log.ActionType,
                    ActionLabel = log.ActionLabel,
                    EntityType = log.EntityType,
                    EntityId = log.EntityId,
                    Actor = !string.IsNullOrWhiteSpace(log.ActualActorUsername)
                        ? log.ActualActorUsername
                        : log.Username,
                    Message = log.Message,
                    Source = log.Source,
                    ReasonCode = ExtractReason(log.Message),
                    OccurredAt = log.OccurredAt,
                    Success = log.Success,
                })
                .ToList();
        }

        private static IReadOnlyList<MonitoringSummaryItem> BuildTopOperators(
            IReadOnlyList<AuditLog> logs
        )
        {
            return logs.Where(log => ScanActionTypes.Contains(log.ActionType))
                .GroupBy(log => string.IsNullOrWhiteSpace(log.Username) ? "system" : log.Username)
                .Select(group => new MonitoringSummaryItem
                {
                    Key = group.Key,
                    Label = group.Key,
                    Count = group.Count(),
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label)
                .Take(MaxSummaryItems)
                .ToList();
        }

        private static IReadOnlyList<MonitoringSummaryItem> BuildTopRejectionReasons(
            IReadOnlyList<AuditLog> logs
        )
        {
            return logs.Where(log => !log.Success && DeniedActionTypes.Contains(log.ActionType))
                .GroupBy(log => ExtractReason(log.Message))
                .Select(group => new MonitoringSummaryItem
                {
                    Key = group.Key,
                    Label = group.Key,
                    Count = group.Count(),
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label)
                .Take(MaxSummaryItems)
                .ToList();
        }

        private static IReadOnlyList<MonitoringSummaryItem> BuildRecentSystemErrors(
            IReadOnlyList<AuditLog> logs
        )
        {
            return logs.Where(log =>
                    !log.Success && SystemErrorActionTypes.Contains(log.ActionType)
                )
                .Take(MaxSystemErrors)
                .Select(log => new MonitoringSummaryItem
                {
                    Key = log.EntityId,
                    Label = log.ActionLabel,
                    ActionType = log.ActionType,
                    Message = log.Message,
                    Source = log.Source,
                    OccurredAt = log.OccurredAt,
                    Success = log.Success,
                })
                .ToList();
        }

        private static string NormalizeRange(string? range)
        {
            return range?.Trim().ToLowerInvariant() switch
            {
                "7d" => "7d",
                "30d" => "30d",
                _ => "today",
            };
        }

        private static DateTime GetPeriodStart(string range, DateTime now)
        {
            return range switch
            {
                "7d" => now.Date.AddDays(-6),
                "30d" => now.Date.AddDays(-29),
                _ => now.Date,
            };
        }

        private static string GetRangeLabel(string range)
        {
            return range switch
            {
                "7d" => "آخر 7 أيام",
                "30d" => "آخر 30 يوم",
                _ => "اليوم",
            };
        }

        private static string ExtractReason(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "غير محدد";
            }

            var markerIndex = message.LastIndexOf("السبب:", StringComparison.Ordinal);
            var reason = markerIndex >= 0 ? message[(markerIndex + "السبب:".Length)..] : message;

            return reason.Trim().TrimEnd('.', '،', ';');
        }
    }
}
