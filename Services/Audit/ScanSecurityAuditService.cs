using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.DTOs;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Services.Common;

namespace VehiclePermitSystemWeb.Services.Audit
{
    public sealed class ScanSecurityAuditService : IScanSecurityAuditService
    {
        public const string ConcurrentGateUseWarning = "concurrent_gate_use";

        private const int DefaultConcurrentWindowSeconds = 120;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;
        private readonly IConfiguration _configuration;

        public ScanSecurityAuditService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock,
            IConfiguration configuration
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
            _configuration = configuration;
        }

        public string RecordScanResult(
            string entityType,
            string entityId,
            bool allowed,
            string reason,
            PermitScanAuditContext auditContext,
            string? recordedBy = null
        )
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return string.Empty;
            }

            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var occurredAt = _systemClock.UtcNow;
                var actor = string.IsNullOrWhiteSpace(recordedBy)
                    ? auditContext.GateOperatorAccount
                    : recordedBy.Trim();
                var warning = allowed
                    ? DetectConcurrentGateUse(
                        db,
                        entityType,
                        entityId,
                        auditContext,
                        actor,
                        occurredAt
                    )
                    : string.Empty;

                db.AuditLogs.Add(
                    new AuditLog
                    {
                        Username = actor,
                        ActualActorUsername = actor,
                        ActionType = allowed ? "QrScanAccepted" : "QrScanRejected",
                        ActionLabel = allowed ? "مسح رمز مقبول" : "مسح رمز مرفوض",
                        EntityType = entityType,
                        EntityId = entityId,
                        Message = allowed
                            ? $"تم قبول مسح {entityType} من البوابة المحددة."
                            : $"تم رفض مسح {entityType}. السبب: {reason}.",
                        Source = nameof(ScanSecurityAuditService),
                        RecordedBy = actor,
                        OccurredAt = occurredAt,
                        IpAddress = auditContext.IpAddress,
                        Success = allowed,
                        AfterJson = SerializeScanDetails(reason, warning, auditContext),
                    }
                );
                db.SaveChanges();
                return warning;
            }
            catch
            {
                // The gate decision must not fail when security telemetry is unavailable.
                return string.Empty;
            }
        }

        private string DetectConcurrentGateUse(
            ApplicationDbContext db,
            string entityType,
            string entityId,
            PermitScanAuditContext auditContext,
            string actor,
            DateTime occurredAt
        )
        {
            if (string.IsNullOrWhiteSpace(auditContext.DeviceId))
            {
                return string.Empty;
            }

            var configuredWindow = _configuration.GetValue<int?>(
                "Security:QrConcurrentGateWindowSeconds"
            );
            var windowSeconds = Math.Clamp(
                configuredWindow ?? DefaultConcurrentWindowSeconds,
                30,
                900
            );
            var windowStart = occurredAt.AddSeconds(-windowSeconds);
            var previousAcceptedScans = db
                .AuditLogs.AsNoTracking()
                .Where(log =>
                    log.EntityType == entityType
                    && log.EntityId == entityId
                    && log.ActionType == "QrScanAccepted"
                    && log.OccurredAt >= windowStart
                )
                .OrderByDescending(log => log.OccurredAt)
                .Take(10)
                .ToList();

            var previous = previousAcceptedScans
                .Select(log => new { Log = log, Details = ReadScanDetails(log.AfterJson) })
                .FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item.Details.DeviceId)
                    && !string.Equals(
                        item.Details.DeviceId,
                        auditContext.DeviceId,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            if (previous == null)
            {
                return string.Empty;
            }

            var existingAlert = db.AuditLogs.AsNoTracking().Any(log =>
                log.EntityType == entityType
                && log.EntityId == entityId
                && log.ActionType == "QrConcurrentGateUse"
                && log.OccurredAt >= windowStart
            );
            if (!existingAlert)
            {
                db.AuditLogs.Add(
                    new AuditLog
                    {
                        Username = actor,
                        ActualActorUsername = actor,
                        ActionType = "QrConcurrentGateUse",
                        ActionLabel = "استخدام متزامن للرمز",
                        EntityType = entityType,
                        EntityId = entityId,
                        Message = "تم رصد استخدام الرمز من جهازين مختلفين خلال فترة قصيرة.",
                        Source = nameof(ScanSecurityAuditService),
                        RecordedBy = actor,
                        OccurredAt = occurredAt,
                        IpAddress = auditContext.IpAddress,
                        Success = false,
                        BeforeJson = JsonSerializer.Serialize(
                            new
                            {
                                previous.Details.GateName,
                                previous.Details.DeviceId,
                                previous.Log.OccurredAt,
                            }
                        ),
                        AfterJson = JsonSerializer.Serialize(
                            new
                            {
                                auditContext.GateName,
                                auditContext.DeviceId,
                                WindowSeconds = windowSeconds,
                            }
                        ),
                    }
                );
            }

            return ConcurrentGateUseWarning;
        }

        private static string SerializeScanDetails(
            string reason,
            string warning,
            PermitScanAuditContext auditContext
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    Reason = reason,
                    SecurityWarning = warning,
                    auditContext.GateName,
                    auditContext.GateOperatorAccount,
                    auditContext.DeviceId,
                    auditContext.ExecutionMethod,
                    auditContext.IsAutomated,
                }
            );
        }

        private static ScanDetails ReadScanDetails(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return ScanDetails.Empty;
            }

            try
            {
                return JsonSerializer.Deserialize<ScanDetails>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    )
                    ?? ScanDetails.Empty;
            }
            catch (JsonException)
            {
                return ScanDetails.Empty;
            }
        }

        private sealed record ScanDetails(string GateName, string DeviceId)
        {
            public static ScanDetails Empty { get; } = new(string.Empty, string.Empty);
        }
    }
}
