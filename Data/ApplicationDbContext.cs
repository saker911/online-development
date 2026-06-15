using Microsoft.EntityFrameworkCore;
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

namespace VehiclePermitSystemWeb.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Permit> Permits => Set<Permit>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<VisitCompanion> VisitCompanions => Set<VisitCompanion>();
        public DbSet<PermitActivity> PermitActivities => Set<PermitActivity>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
        public DbSet<UserActivity> UserActivities => Set<UserActivity>();
        public DbSet<SessionRecord> SessionRecords => Set<SessionRecord>();
        public DbSet<Delegation> Delegations => Set<Delegation>();
        public DbSet<DelegationPermission> DelegationPermissions => Set<DelegationPermission>();
        public DbSet<AdministrationSettings> AdministrationSettings =>
            Set<AdministrationSettings>();
        public DbSet<DisplayDevice> DisplayDevices => Set<DisplayDevice>();
        public DbSet<DisplaySecuritySettings> DisplaySecuritySettings =>
            Set<DisplaySecuritySettings>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Permit>().HasKey(x => x.PermitNumber);
            modelBuilder.Entity<Permit>().HasIndex(x => x.PermitNumber).IsUnique();
            modelBuilder.Entity<Permit>().HasIndex(x => x.ArchivedAt);
            modelBuilder.Entity<Visit>().HasKey(x => x.VisitId);
            modelBuilder.Entity<VisitCompanion>().HasKey(x => x.Id);
            modelBuilder.Entity<PermitActivity>().HasKey(x => x.Id);
            modelBuilder.Entity<Department>().HasKey(x => x.Id);
            modelBuilder.Entity<UserAccount>().HasKey(x => x.Username);
            modelBuilder.Entity<UserActivity>().HasKey(x => x.Id);
            modelBuilder.Entity<SessionRecord>().HasKey(x => x.SessionId);
            modelBuilder.Entity<Delegation>().HasKey(x => x.Id);
            modelBuilder.Entity<DelegationPermission>().HasKey(x => x.Id);
            modelBuilder.Entity<AdministrationSettings>().HasKey(x => x.Id);
            modelBuilder.Entity<DisplayDevice>().HasKey(x => x.Id);
            modelBuilder.Entity<DisplaySecuritySettings>().HasKey(x => x.Id);

            modelBuilder.Entity<Permit>().Property(x => x.PermitNumber).HasMaxLength(32);
            modelBuilder.Entity<Permit>().Property(x => x.PermitType).HasMaxLength(24);
            modelBuilder.Entity<Permit>().Property(x => x.AccessMode).HasMaxLength(24);
            modelBuilder.Entity<Permit>().Property(x => x.CurrentState).HasMaxLength(16);
            modelBuilder.Entity<Permit>().Property(x => x.VisitLocation).HasMaxLength(256);
            modelBuilder.Entity<Permit>().Property(x => x.LeaveReason).HasMaxLength(256);
            modelBuilder
                .Entity<Permit>()
                .Property(x => x.DailyLeaveScheduleReason)
                .HasMaxLength(256);
            modelBuilder
                .Entity<Permit>()
                .Property(x => x.PendingUnauthorizedExitSequenceId)
                .HasMaxLength(64);
            modelBuilder.Entity<Visit>().Property(x => x.VisitId).HasMaxLength(32);
            modelBuilder.Entity<Visit>().Property(x => x.VisitorName).HasMaxLength(128);
            modelBuilder.Entity<Visit>().Property(x => x.VisitLocation).HasMaxLength(256);
            modelBuilder.Entity<Visit>().Property(x => x.NationalId).HasMaxLength(32);
            modelBuilder.Entity<Visit>().Property(x => x.PhoneNumber).HasMaxLength(32);
            modelBuilder.Entity<Visit>().Property(x => x.Purpose).HasMaxLength(256);
            modelBuilder.Entity<Visit>().Property(x => x.HostName).HasMaxLength(128);
            modelBuilder.Entity<Visit>().Property(x => x.VisitedPersonName).HasMaxLength(128);
            modelBuilder.Entity<Visit>().Property(x => x.VisitedPersonType).HasMaxLength(32);
            modelBuilder.Entity<Visit>().Property(x => x.ApprovalStatus).HasMaxLength(32);
            modelBuilder.Entity<Visit>().Property(x => x.VisitApproverUsername).HasMaxLength(64);
            modelBuilder.Entity<VisitCompanion>().Property(x => x.VisitId).HasMaxLength(32);
            modelBuilder.Entity<VisitCompanion>().Property(x => x.FullName).HasMaxLength(128);
            modelBuilder.Entity<VisitCompanion>().Property(x => x.NationalId).HasMaxLength(32);
            modelBuilder.Entity<VisitCompanion>().Property(x => x.PhoneNumber).HasMaxLength(32);
            modelBuilder.Entity<VisitCompanion>().Property(x => x.Relationship).HasMaxLength(64);
            modelBuilder.Entity<PermitActivity>().Property(x => x.PermitNumber).HasMaxLength(32);
            modelBuilder.Entity<PermitActivity>().Property(x => x.DriverName).HasMaxLength(128);
            modelBuilder.Entity<PermitActivity>().Property(x => x.NationalId).HasMaxLength(32);
            modelBuilder.Entity<PermitActivity>().Property(x => x.DepartmentName).HasMaxLength(128);
            modelBuilder.Entity<PermitActivity>().Property(x => x.ActionType).HasMaxLength(32);
            modelBuilder.Entity<PermitActivity>().Property(x => x.ActionLabel).HasMaxLength(64);
            modelBuilder.Entity<PermitActivity>().Property(x => x.Message).HasMaxLength(256);
            modelBuilder.Entity<PermitActivity>().Property(x => x.Source).HasMaxLength(64);
            modelBuilder.Entity<PermitActivity>().Property(x => x.RecordedBy).HasMaxLength(128);
            modelBuilder.Entity<PermitActivity>().Property(x => x.SequenceId).HasMaxLength(64);
            modelBuilder
                .Entity<PermitActivity>()
                .Property(x => x.ClassificationStatus)
                .HasMaxLength(32);
            modelBuilder.Entity<PermitActivity>().Property(x => x.GateName).HasMaxLength(128);
            modelBuilder
                .Entity<PermitActivity>()
                .Property(x => x.GateOperatorName)
                .HasMaxLength(128);
            modelBuilder
                .Entity<PermitActivity>()
                .Property(x => x.GateOperatorAccount)
                .HasMaxLength(64);
            modelBuilder.Entity<PermitActivity>().Property(x => x.DeviceId).HasMaxLength(256);
            modelBuilder.Entity<PermitActivity>().Property(x => x.IpAddress).HasMaxLength(64);
            modelBuilder.Entity<PermitActivity>().Property(x => x.ExecutionMethod).HasMaxLength(32);
            modelBuilder.Entity<Department>().Property(x => x.Name).HasMaxLength(128);
            modelBuilder.Entity<Department>().Property(x => x.ManagerUsername).HasMaxLength(64);
            modelBuilder.Entity<Department>().Property(x => x.ManagerDisplayName).HasMaxLength(128);
            modelBuilder.Entity<Department>().HasIndex(x => x.Name).IsUnique();
            modelBuilder
                .Entity<Visit>()
                .HasMany(x => x.Companions)
                .WithOne(x => x.Visit)
                .HasForeignKey(x => x.VisitId)
                .HasPrincipalKey(x => x.VisitId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<VisitCompanion>().HasIndex(x => x.VisitId);
            modelBuilder
                .Entity<Permit>()
                .HasMany(x => x.Activities)
                .WithOne(x => x.Permit)
                .HasForeignKey(x => x.PermitNumber)
                .HasPrincipalKey(x => x.PermitNumber)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder
                .Entity<PermitActivity>()
                .HasIndex(x => new { x.PermitNumber, x.OccurredAt });
            modelBuilder.Entity<PermitActivity>().HasIndex(x => x.OccurredAt);
            modelBuilder.Entity<AuditLog>().HasKey(x => x.Id);
            modelBuilder.Entity<AuditLog>().Property(x => x.Username).HasMaxLength(64);
            modelBuilder.Entity<AuditLog>().Property(x => x.ActionType).HasMaxLength(64);
            modelBuilder.Entity<AuditLog>().Property(x => x.ActionLabel).HasMaxLength(128);
            modelBuilder.Entity<AuditLog>().Property(x => x.EntityType).HasMaxLength(64);
            modelBuilder.Entity<AuditLog>().Property(x => x.EntityId).HasMaxLength(128);
            modelBuilder.Entity<AuditLog>().Property(x => x.Message).HasMaxLength(512);
            modelBuilder.Entity<AuditLog>().Property(x => x.Source).HasMaxLength(128);
            modelBuilder.Entity<AuditLog>().Property(x => x.RecordedBy).HasMaxLength(128);
            modelBuilder.Entity<AuditLog>().Property(x => x.IpAddress).HasMaxLength(64);
            modelBuilder.Entity<AuditLog>().Property(x => x.ActualActorUsername).HasMaxLength(64);
            modelBuilder.Entity<AuditLog>().Property(x => x.DelegatedFromUsername).HasMaxLength(64);
            modelBuilder.Entity<AuditLog>().Property(x => x.BeforeJson).HasMaxLength(2000);
            modelBuilder.Entity<AuditLog>().Property(x => x.AfterJson).HasMaxLength(2000);
            modelBuilder.Entity<UserActivity>().Property(x => x.Username).HasMaxLength(64);
            modelBuilder.Entity<UserActivity>().Property(x => x.DisplayName).HasMaxLength(128);
            modelBuilder.Entity<UserActivity>().Property(x => x.ActionType).HasMaxLength(32);
            modelBuilder.Entity<UserActivity>().Property(x => x.ActionLabel).HasMaxLength(64);
            modelBuilder.Entity<UserActivity>().Property(x => x.Message).HasMaxLength(256);
            modelBuilder.Entity<UserActivity>().Property(x => x.Source).HasMaxLength(64);
            modelBuilder.Entity<UserActivity>().Property(x => x.RecordedBy).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.Username).HasMaxLength(64);
            modelBuilder.Entity<UserAccount>().Property(x => x.Role).HasMaxLength(32);
            modelBuilder.Entity<UserAccount>().Property(x => x.DisplayName).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.FullName).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.Department).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.EmployeeNumber).HasMaxLength(64);
            modelBuilder.Entity<UserAccount>().Property(x => x.JobTitle).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.OperatorBadgeCode).HasMaxLength(64);
            modelBuilder.Entity<UserAccount>().Property(x => x.OperatorPinHash).HasMaxLength(256);
            modelBuilder.Entity<UserAccount>().Property(x => x.OperatorPinSalt).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.PhoneNumber).HasMaxLength(32);
            modelBuilder.Entity<UserAccount>().Property(x => x.Email).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.ManagerUsername).HasMaxLength(64);
            modelBuilder.Entity<UserAccount>().Property(x => x.IsSuperAdmin).HasDefaultValue(false);
            modelBuilder
                .Entity<UserAccount>()
                .Property(x => x.CanApproveVisits)
                .HasDefaultValue(false);
            modelBuilder
                .Entity<UserAccount>()
                .Property(x => x.CanManageDelegations)
                .HasDefaultValue(false);
            modelBuilder.Entity<Delegation>().Property(x => x.DelegationNumber).HasMaxLength(32);
            modelBuilder.Entity<Delegation>().Property(x => x.DelegatorUsername).HasMaxLength(64);
            modelBuilder.Entity<Delegation>().Property(x => x.DelegateeUsername).HasMaxLength(64);
            modelBuilder.Entity<Delegation>().Property(x => x.ScopeType).HasMaxLength(16);
            modelBuilder.Entity<Delegation>().Property(x => x.TimeZoneId).HasMaxLength(128);
            modelBuilder.Entity<Delegation>().Property(x => x.Status).HasMaxLength(16);
            modelBuilder.Entity<Delegation>().Property(x => x.Notes).HasMaxLength(512);
            modelBuilder.Entity<Delegation>().Property(x => x.CreatedByUsername).HasMaxLength(64);
            modelBuilder
                .Entity<Delegation>()
                .Property(x => x.LastUpdatedByUsername)
                .HasMaxLength(64);
            modelBuilder.Entity<Delegation>().Property(x => x.CancelledByUsername).HasMaxLength(64);
            modelBuilder.Entity<Delegation>().Property(x => x.CancelReason).HasMaxLength(256);
            modelBuilder.Entity<Delegation>().HasIndex(x => x.DelegationNumber).IsUnique();
            modelBuilder.Entity<Delegation>().HasIndex(x => new { x.DelegateeUsername, x.Status });
            modelBuilder.Entity<Delegation>().HasIndex(x => new { x.DelegatorUsername, x.Status });
            modelBuilder
                .Entity<DelegationPermission>()
                .Property(x => x.PermissionKey)
                .HasMaxLength(64);
            modelBuilder
                .Entity<DelegationPermission>()
                .HasIndex(x => new { x.DelegationId, x.PermissionKey })
                .IsUnique();
            modelBuilder
                .Entity<Delegation>()
                .HasMany(x => x.Permissions)
                .WithOne(x => x.Delegation)
                .HasForeignKey(x => x.DelegationId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Permit>().Property(x => x.CreatedBy).HasMaxLength(128);
            modelBuilder.Entity<SessionRecord>().Property(x => x.SessionId).HasMaxLength(128);
            modelBuilder.Entity<SessionRecord>().Property(x => x.Username).HasMaxLength(64);
            modelBuilder
                .Entity<AdministrationSettings>()
                .Property(x => x.OrganizationName)
                .HasMaxLength(256);
            modelBuilder
                .Entity<AdministrationSettings>()
                .Property(x => x.DepartmentName)
                .HasMaxLength(256);
            modelBuilder
                .Entity<AdministrationSettings>()
                .Property(x => x.GeneralManagerUsername)
                .HasMaxLength(64);
            modelBuilder
                .Entity<AdministrationSettings>()
                .Property(x => x.DisplayAccessKey)
                .HasMaxLength(128);
            modelBuilder
                .Entity<AdministrationSettings>()
                .Property(x => x.OfficialWorkDaysCsv)
                .HasMaxLength(256);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.ScreenName).HasMaxLength(128);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.ScreenLocation).HasMaxLength(128);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.Description).HasMaxLength(512);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.Status).HasMaxLength(24);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.DeviceTokenHash).HasMaxLength(128);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.RequestCode).HasMaxLength(64);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.IpAddress).HasMaxLength(64);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.LastIpAddress).HasMaxLength(64);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.UserAgent).HasMaxLength(512);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.ApprovedByUserId).HasMaxLength(64);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.Notes).HasMaxLength(512);
            modelBuilder.Entity<DisplayDevice>().HasIndex(x => x.RequestCode).IsUnique();
            modelBuilder
                .Entity<DisplaySecuritySettings>()
                .Property(x => x.SetupKeyHash)
                .HasMaxLength(128);
        }
    }
}
