using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Models.DTOs;
using VehiclePermitSystemWeb.Services.Audit;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Security)]
[Trait("Area", TestAreas.VehicleScan)]
public sealed class ScanSecurityAuditTests
{
    [Fact]
    public async Task AcceptedScanFromAnotherDeviceCreatesSecurityAlert()
    {
        await using var fixture = new TestFixture();
        var firstWarning = fixture.ScanSecurityAuditService.RecordScanResult(
            "Visit",
            "V900",
            true,
            "entry_recorded",
            CreateContext("البوابة الشمالية", "device-a"),
            "guard-a"
        );
        var secondWarning = fixture.ScanSecurityAuditService.RecordScanResult(
            "Visit",
            "V900",
            true,
            "exit_recorded",
            CreateContext("البوابة الجنوبية", "device-b"),
            "guard-b"
        );

        Assert.Empty(firstWarning);
        Assert.Equal(ScanSecurityAuditService.ConcurrentGateUseWarning, secondWarning);

        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var logs = await db
            .AuditLogs.AsNoTracking()
            .Where(log => log.EntityType == "Visit" && log.EntityId == "V900")
            .OrderBy(log => log.Id)
            .ToListAsync();

        Assert.Equal(2, logs.Count(log => log.ActionType == "QrScanAccepted"));
        var alert = Assert.Single(logs.Where(log => log.ActionType == "QrConcurrentGateUse"));
        Assert.False(alert.Success);
        Assert.Contains("device-a", alert.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("device-b", alert.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedScanIsAuditedWithoutConcurrentUseAlert()
    {
        await using var fixture = new TestFixture();
        var warning = fixture.ScanSecurityAuditService.RecordScanResult(
            "Permit",
            "PERMIT-00900",
            false,
            "site_access_denied",
            CreateContext("البوابة الشمالية", "device-a"),
            "guard-a"
        );

        Assert.Empty(warning);

        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var rejection = await db.AuditLogs.AsNoTracking().SingleAsync(log =>
            log.EntityType == "Permit"
            && log.EntityId == "PERMIT-00900"
            && log.ActionType == "QrScanRejected"
        );
        Assert.False(rejection.Success);
        Assert.Contains("site_access_denied", rejection.AfterJson, StringComparison.Ordinal);
        Assert.False(
            await db.AuditLogs.AsNoTracking().AnyAsync(log =>
                log.EntityId == "PERMIT-00900" && log.ActionType == "QrConcurrentGateUse"
            )
        );
    }

    private static PermitScanAuditContext CreateContext(string gateName, string deviceId)
    {
        return new PermitScanAuditContext
        {
            GateName = gateName,
            GateOperatorName = "حارس اختبار",
            GateOperatorAccount = "guard",
            DeviceId = deviceId,
            IpAddress = "127.0.0.1",
            ExecutionMethod = "camera",
        };
    }
}
