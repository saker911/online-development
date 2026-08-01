using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Security;
using Xunit;

namespace PermitBehaviorChecks;

public sealed class VisitApprovalRoleTests
{
    [Fact]
    public async Task SecurityManagerCanApproveVisitsAcrossDepartments()
    {
        await using var fixture = new TestFixture();
        const string username = "security-manager-approval-test";
        const string visitId = "SECURITY-MANAGER-VISIT";

        using (var db = fixture.DbFactory.CreateDbContext())
        {
            var securityManager = new UserAccount
            {
                Username = username,
                DisplayName = "Security manager",
                FullName = "Security manager",
                IsActive = true,
                Role = AppRoles.SecurityManager,
            };
            AppPermissions.ApplyRoleDefaults(securityManager);
            db.UserAccounts.Add(securityManager);
            db.Visits.Add(
                new Visit
                {
                    VisitId = visitId,
                    VisitorName = "Approval test visitor",
                    VisitLocation = "A different department",
                    VisitDate = fixture.Clock.LocalNow,
                    ApprovalStatus = "Pending",
                    Status = "Active",
                }
            );
            db.SaveChanges();
        }

        fixture.VisitService.UpdateVisitApprovalStatus(visitId, "Approved", username);

        using var verificationDb = fixture.DbFactory.CreateDbContext();
        Assert.Equal(
            "Approved",
            verificationDb.Visits.AsNoTracking().Single(item => item.VisitId == visitId)
                .ApprovalStatus
        );
    }
}
