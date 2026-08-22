using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Models.Entities;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Visits)]
public sealed class VisitQueueWorkflowTests
{
    [Fact]
    public async Task GateArrivalStartsDepartmentQueueWorkflow()
    {
        await using var fixture = new TestFixture();
        const string visitId = "QUEUE-VISIT-001";

        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var tenant = db.Tenants.Single(item => item.TenantId == TenantDefaults.DefaultTenantId);
            tenant.VisitsServiceEnabled = true;
            tenant.SelfServiceEnabled = true;
            tenant.QueueServiceEnabled = true;
            var department = new Department
            {
                Name = "الخدمات المالية",
                IsActive = true,
                AcceptsVisitors = true,
            };
            var site = new WorkplaceSite
            {
                Name = "المقر الرئيسي",
                Code = "HQ",
                VisitsEnabled = true,
                SelfServiceEnabled = true,
                QueueEnabled = true,
                IsActive = true,
            };
            db.Departments.Add(department);
            db.WorkplaceSites.Add(site);
            db.SaveChanges();
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
                    DepartmentId = department.Id,
                    WorkplaceSiteId = site.Id,
                    VisitedPersonName = department.Name,
                }
            );
            db.SaveChanges();
        }

        fixture.VisitService.UpdateVisitApprovalStatus(visitId, "Approved", "tester");
        var approved = fixture.VisitService.GetVisitById(visitId);
        Assert.NotNull(approved);
        Assert.Equal(string.Empty, approved!.QueueStatus);
        Assert.Null(approved.QueuedAtUtc);
        Assert.False(
            fixture.VisitService.UpdateQueueStatus(
                visitId,
                Visit.QueueStatusCalled,
                "tester"
            )
        );

        var arrival = fixture.VisitService.RecordVisitScan(visitId, "gate");
        Assert.True(arrival.allowed);
        Assert.Equal("entry_recorded", arrival.reason);

        approved = fixture.VisitService.GetVisitById(visitId);
        Assert.NotNull(approved);
        Assert.Equal("Inside", approved!.Status);
        Assert.Equal(Visit.QueueStatusWaiting, approved.QueueStatus);
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
        Assert.False(
            fixture.VisitService.UpdateQueueStatus(
                visitId,
                Visit.QueueStatusServing,
                "another-operator"
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
        Assert.Equal("tester", completed.ServiceOperatorUsername);
        Assert.Equal("Tester", completed.ServiceOperatorDisplayName);
    }

    [Fact]
    public async Task ApprovalAllocatesThirtyMinuteSlotsUsingDepartmentStaffCapacity()
    {
        await using var fixture = new TestFixture();
        const string departmentName = "خدمة العملاء";
        var requestedTime = fixture.Clock.LocalNow.AddHours(1);

        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var department = new Department
            {
                Name = departmentName,
                IsActive = true,
                AcceptsVisitors = true,
            };
            db.Departments.Add(department);
            for (var index = 1; index <= 4; index++)
            {
                db.UserAccounts.Add(
                    new UserAccount
                    {
                        Username = $"reception-{index}",
                        DisplayName = $"موظف استقبال {index}",
                        FullName = $"موظف استقبال {index}",
                        Department = departmentName,
                        JobTitle = "موظف استقبال",
                        PhoneNumber = $"050000000{index}",
                        Role = "Receptionist",
                        IsActive = true,
                        CanCreateVisit = true,
                    }
                );
            }
            db.SaveChanges();

            for (var index = 1; index <= 5; index++)
            {
                db.Visits.Add(
                    new Visit
                    {
                        VisitId = $"SCHEDULE-{index}",
                        VisitorName = $"زائر {index}",
                        VisitLocation = "الاستقبال",
                        VisitDate = requestedTime,
                        ApprovalStatus = "Pending",
                        Status = "Active",
                        DepartmentId = department.Id,
                        VisitedPersonName = departmentName,
                    }
                );
            }
            db.SaveChanges();
        }

        for (var index = 1; index <= 5; index++)
        {
            fixture.VisitService.UpdateVisitApprovalStatus(
                $"SCHEDULE-{index}",
                "Approved",
                "tester"
            );
        }

        var earlyArrival = fixture.VisitService.RecordVisitScan("SCHEDULE-5", "gate");
        Assert.False(earlyArrival.allowed);
        Assert.Equal("visit_not_started", earlyArrival.reason);

        using var verificationDb = fixture.DbFactory.CreateDbContext();
        var visits = verificationDb.Visits.AsNoTracking()
            .Where(visit => visit.VisitId.StartsWith("SCHEDULE-"))
            .OrderBy(visit => visit.VisitId)
            .ToList();
        Assert.All(visits.Take(4), visit => Assert.Equal(requestedTime, visit.VisitDate));
        Assert.Equal(requestedTime.AddMinutes(30), visits[4].VisitDate);
        Assert.All(visits, visit => Assert.Equal(30, visit.ServiceDurationMinutes));
        Assert.All(visits, visit => Assert.Equal(requestedTime, visit.RequestedVisitDate));
    }

    [Fact]
    public async Task MissedAppointmentDoesNotBlockTheNextScheduledVisitor()
    {
        await using var fixture = new TestFixture();
        var requestedTime = fixture.Clock.LocalNow.AddHours(1);
        int departmentId;

        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var department = new Department
            {
                Name = "المراجعات",
                IsActive = true,
                AcceptsVisitors = true,
            };
            db.Departments.Add(department);
            db.SaveChanges();
            departmentId = department.Id;
            db.Visits.AddRange(
                CreatePendingVisit("NO-SHOW-1", requestedTime, departmentId, department.Name),
                CreatePendingVisit("NO-SHOW-2", requestedTime, departmentId, department.Name)
            );
            db.SaveChanges();
        }

        fixture.VisitService.UpdateVisitApprovalStatus("NO-SHOW-1", "Approved", "tester");
        fixture.VisitService.UpdateVisitApprovalStatus("NO-SHOW-2", "Approved", "tester");
        fixture.Clock.SetLocalNow(requestedTime.AddMinutes(21));
        var missedArrival = fixture.VisitService.RecordVisitScan("NO-SHOW-1", "gate");

        var visits = fixture.VisitService.GetAllVisits().ToDictionary(visit => visit.VisitId);
        Assert.False(missedArrival.allowed);
        Assert.Equal("visit_no_show", missedArrival.reason);
        Assert.Equal("Completed", visits["NO-SHOW-1"].Status);
        Assert.Equal(string.Empty, visits["NO-SHOW-1"].QueueStatus);
        Assert.Equal("Active", visits["NO-SHOW-2"].Status);
        Assert.Equal(requestedTime.AddMinutes(30), visits["NO-SHOW-2"].VisitDate);
    }

    private static Visit CreatePendingVisit(
        string visitId,
        DateTime visitDate,
        int departmentId,
        string departmentName
    ) =>
        new()
        {
            VisitId = visitId,
            VisitorName = visitId,
            VisitLocation = "الاستقبال",
            VisitDate = visitDate,
            ApprovalStatus = "Pending",
            Status = "Active",
            DepartmentId = departmentId,
            VisitedPersonName = departmentName,
        };
}
