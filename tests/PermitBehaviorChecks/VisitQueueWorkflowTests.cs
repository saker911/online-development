using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Models.Entities;
using Xunit;

namespace PermitBehaviorChecks;

public sealed class VisitQueueWorkflowTests
{
    [Fact]
    public async Task ApprovedSelfServiceVisitCompletesQueueWorkflow()
    {
        await using var fixture = new TestFixture();
        const string visitId = "QUEUE-VISIT-001";

        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var tenant = db.Tenants.Single(item => item.TenantId == TenantDefaults.DefaultTenantId);
            tenant.VisitsServiceEnabled = true;
            tenant.SelfServiceEnabled = true;
            tenant.QueueServiceEnabled = true;
            db.Visits.Add(
                new Visit
                {
                    VisitId = visitId,
                    VisitorName = "زائر الطابور",
                    VisitLocation = "الاستقبال",
                    VisitDate = fixture.Clock.LocalNow.AddMinutes(10),
                    ApprovalStatus = "Pending",
                    Status = "Active",
                    RequestSource = Visit.RequestSourcePublicSelfService,
                }
            );
            db.SaveChanges();
        }

        fixture.VisitService.UpdateVisitApprovalStatus(visitId, "Approved", "tester");
        var approved = fixture.VisitService.GetVisitById(visitId);
        Assert.NotNull(approved);
        Assert.Equal(Visit.QueueStatusWaiting, approved!.QueueStatus);
        Assert.NotNull(approved.QueuedAtUtc);
        Assert.StartsWith("V-", approved.QueueTicketNumber);

        Assert.True(
            fixture.VisitService.UpdateQueueStatus(
                visitId,
                Visit.QueueStatusCalled,
                "tester"
            )
        );
        Assert.True(
            fixture.VisitService.UpdateQueueStatus(
                visitId,
                Visit.QueueStatusServing,
                "tester"
            )
        );
        Assert.True(
            fixture.VisitService.UpdateQueueStatus(
                visitId,
                Visit.QueueStatusCompleted,
                "tester"
            )
        );

        using var verificationDb = fixture.DbFactory.CreateDbContext();
        var completed = verificationDb.Visits.AsNoTracking().Single(item => item.VisitId == visitId);
        Assert.Equal(Visit.QueueStatusCompleted, completed.QueueStatus);
        Assert.NotNull(completed.CalledAtUtc);
        Assert.NotNull(completed.ServiceStartedAtUtc);
        Assert.NotNull(completed.QueueCompletedAtUtc);
    }
}
