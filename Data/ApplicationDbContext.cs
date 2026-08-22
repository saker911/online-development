using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
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
using VehiclePermitSystemWeb.Models.ViewModels.Workplace;
using VehiclePermitSystemWeb.Services.Tenants;

namespace VehiclePermitSystemWeb.Data
{
    public class ApplicationDbContext : DbContext, IDataProtectionKeyContext
    {
        private readonly string _tenantId;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : this(options, new DefaultTenantContext()) { }

        [ActivatorUtilitiesConstructor]
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext tenantContext
        )
            : base(options)
        {
            _tenantId = string.IsNullOrWhiteSpace(tenantContext.TenantId)
                ? TenantDefaults.DefaultTenantId
                : tenantContext.TenantId.Trim();
        }

        public string CurrentTenantId => _tenantId;

        public DbSet<Permit> Permits => Set<Permit>();
        public DbSet<Tenant> Tenants => Set<Tenant>();
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
        public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
        public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
        public DbSet<VisitorWorkflowSettings> VisitorWorkflowSettings =>
            Set<VisitorWorkflowSettings>();
        public DbSet<DisplayDevice> DisplayDevices => Set<DisplayDevice>();
        public DbSet<DisplaySecuritySettings> DisplaySecuritySettings =>
            Set<DisplaySecuritySettings>();
        public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
        public DbSet<ExternalUserLogin> ExternalUserLogins => Set<ExternalUserLogin>();
        public DbSet<LoginAttemptRecord> LoginAttemptRecords => Set<LoginAttemptRecord>();
        public DbSet<SignupAttemptRecord> SignupAttemptRecords => Set<SignupAttemptRecord>();
        public DbSet<EmailNotificationOutbox> EmailNotificationOutbox =>
            Set<EmailNotificationOutbox>();
        public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();
        public DbSet<WorkplaceSite> WorkplaceSites => Set<WorkplaceSite>();
        public DbSet<WorkplaceSiteEntrance> WorkplaceSiteEntrances =>
            Set<WorkplaceSiteEntrance>();
        public DbSet<PersonProfile> PersonProfiles => Set<PersonProfile>();
        public DbSet<PersonPhoto> PersonPhotos => Set<PersonPhoto>();
        public DbSet<EmergencySession> EmergencySessions => Set<EmergencySession>();
        public DbSet<EmergencySessionMember> EmergencySessionMembers =>
            Set<EmergencySessionMember>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Permit>().HasKey(x => x.PermitNumber);
            modelBuilder.Entity<Tenant>().HasKey(x => x.TenantId);
            modelBuilder.Entity<Permit>().HasIndex(x => x.PermitNumber).IsUnique();
            modelBuilder.Entity<Permit>().HasIndex(x => x.ArchivedAt);
            modelBuilder.Entity<Visit>().HasKey(x => x.VisitId);
            modelBuilder.Entity<Visit>().Property(x => x.QueueStatus).HasMaxLength(24);
            modelBuilder.Entity<Visit>().HasIndex(x => new { x.TenantId, x.QueueStatus });
            modelBuilder.Entity<Visit>().HasIndex(x => new { x.TenantId, x.DepartmentId, x.QueueStatus });
            modelBuilder.Entity<Visit>().HasIndex(x => new { x.TenantId, x.DepartmentId, x.VisitDate });
            modelBuilder.Entity<VisitCompanion>().HasKey(x => x.Id);
            modelBuilder.Entity<PermitActivity>().HasKey(x => x.Id);
            modelBuilder.Entity<Department>().HasKey(x => x.Id);
            modelBuilder.Entity<Department>().Property(x => x.AcceptsVisitors).HasDefaultValue(true);
            modelBuilder.Entity<UserAccount>().HasKey(x => x.Username);
            modelBuilder.Entity<UserActivity>().HasKey(x => x.Id);
            modelBuilder.Entity<SessionRecord>().HasKey(x => x.SessionId);
            modelBuilder.Entity<Delegation>().HasKey(x => x.Id);
            modelBuilder.Entity<DelegationPermission>().HasKey(x => x.Id);
            modelBuilder.Entity<AdministrationSettings>().HasKey(x => x.Id);
            modelBuilder.Entity<PlatformSettings>().HasKey(x => x.Id);
            modelBuilder.Entity<SubscriptionPlan>().HasKey(x => x.Code);
            modelBuilder.Entity<VisitorWorkflowSettings>().HasKey(x => x.TenantId);
            modelBuilder.Entity<DisplayDevice>().HasKey(x => x.Id);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.AppVersion).HasMaxLength(32);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.Platform).HasMaxLength(96);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.NetworkStatus).HasMaxLength(32);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.CameraStatus).HasMaxLength(32);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.LastHealthError).HasMaxLength(256);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.ConfigurationVersion).HasDefaultValue(1);
            modelBuilder.Entity<DisplayDevice>().Property(x => x.AppliedConfigurationVersion).HasDefaultValue(0);
            modelBuilder.Entity<DisplaySecuritySettings>().HasKey(x => x.Id);
            modelBuilder.Entity<ExternalUserLogin>().HasKey(x => x.Id);
            modelBuilder.Entity<LoginAttemptRecord>().HasKey(x => x.KeyHash);
            modelBuilder.Entity<SignupAttemptRecord>().HasKey(x => x.KeyHash);
            modelBuilder.Entity<EmailNotificationOutbox>().HasKey(x => x.Id);
            modelBuilder.Entity<InAppNotification>().HasKey(x => x.Id);
            modelBuilder.Entity<WorkplaceSite>().HasKey(x => x.Id);
            modelBuilder.Entity<WorkplaceSiteEntrance>().HasKey(x => x.Id);
            modelBuilder.Entity<PersonProfile>().HasKey(x => x.Id);
            modelBuilder.Entity<PersonPhoto>().HasKey(x => x.PersonProfileId);
            modelBuilder.Entity<EmergencySession>().HasKey(x => x.Id);
            modelBuilder.Entity<EmergencySessionMember>().HasKey(x => x.Id);

            modelBuilder.Entity<Tenant>().Property(x => x.TenantId).HasMaxLength(64);
            modelBuilder.Entity<Tenant>().Property(x => x.Name).HasMaxLength(256);
            modelBuilder.Entity<Tenant>().Property(x => x.Slug).HasMaxLength(256);
            modelBuilder.Entity<Tenant>().Property(x => x.OrganizationReference).HasMaxLength(32);
            modelBuilder.Entity<Tenant>().Property(x => x.SubscriptionStatus).HasMaxLength(32);
            modelBuilder.Entity<Tenant>().Property(x => x.PlanName).HasMaxLength(128);
            modelBuilder.Entity<Tenant>().Property(x => x.SignupPlanCode).HasMaxLength(64);
            modelBuilder.Entity<Tenant>().Property(x => x.SignupPlanPrice).HasPrecision(18, 2);
            modelBuilder.Entity<Tenant>().Property(x => x.PermitsServiceEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.VisitsServiceEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.SelfServiceEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.QueueServiceEnabled).HasDefaultValue(false);
            modelBuilder.Entity<Tenant>().Property(x => x.GateServiceEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.NotificationCenterEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.PermitNotificationsEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.VisitNotificationsEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.SecurityAlertsEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.FailedOperationAlertsEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.UnauthorizedMovementAlertsEnabled).HasDefaultValue(true);
            modelBuilder.Entity<Tenant>().Property(x => x.NotificationRetentionDays).HasDefaultValue(90);
            modelBuilder.Entity<Tenant>().Property(x => x.EmailOutboxRetentionDays).HasDefaultValue(30);
            modelBuilder.Entity<Tenant>().Property(x => x.AuditLogRetentionDays).HasDefaultValue(365);
            modelBuilder.Entity<Tenant>().HasIndex(x => x.Slug).IsUnique();
            modelBuilder.Entity<PlatformSettings>().Property(x => x.ProviderName).HasMaxLength(256);
            modelBuilder
                .Entity<PlatformSettings>()
                .Property(x => x.CommercialRegistration)
                .HasMaxLength(64);
            modelBuilder.Entity<PlatformSettings>().Property(x => x.VatNumber).HasMaxLength(64);
            modelBuilder
                .Entity<PlatformSettings>()
                .Property(x => x.RegisteredAddress)
                .HasMaxLength(512);
            modelBuilder.Entity<PlatformSettings>().Property(x => x.City).HasMaxLength(128);
            modelBuilder.Entity<PlatformSettings>().Property(x => x.Country).HasMaxLength(128);
            modelBuilder
                .Entity<PlatformSettings>()
                .Property(x => x.OfficialEmail)
                .HasMaxLength(256);
            modelBuilder.Entity<PlatformSettings>().Property(x => x.SupportPhone).HasMaxLength(32);
            modelBuilder
                .Entity<PlatformSettings>()
                .Property(x => x.WhatsAppNumber)
                .HasMaxLength(32);
            modelBuilder.Entity<PlatformSettings>().Property(x => x.WorkingHours).HasMaxLength(256);
            modelBuilder
                .Entity<PlatformSettings>()
                .Property(x => x.DataHostingLocation)
                .HasMaxLength(256);
            modelBuilder.Entity<PlatformSettings>().Property(x => x.BackupPolicy).HasMaxLength(1000);
            modelBuilder.Entity<SubscriptionPlan>().Property(x => x.Code).HasMaxLength(64);
            modelBuilder.Entity<SubscriptionPlan>().Property(x => x.Name).HasMaxLength(96);
            modelBuilder.Entity<SubscriptionPlan>().Property(x => x.Summary).HasMaxLength(320);
            modelBuilder.Entity<SubscriptionPlan>().Property(x => x.Price).HasPrecision(18, 2);
            modelBuilder.Entity<SubscriptionPlan>().Property(x => x.OriginalPrice).HasPrecision(18, 2);
            modelBuilder.Entity<SubscriptionPlan>().Property(x => x.OfferLabel).HasMaxLength(64);
            modelBuilder.Entity<SubscriptionPlan>().Property(x => x.FeaturesJson).HasMaxLength(4000);
            modelBuilder.Entity<SubscriptionPlan>().HasIndex(x => x.SortOrder);
            modelBuilder
                .Entity<VisitorWorkflowSettings>()
                .Property(x => x.TenantId)
                .HasMaxLength(64);
            modelBuilder
                .Entity<VisitorWorkflowSettings>()
                .Property(x => x.TemplateKey)
                .HasMaxLength(24);
            modelBuilder
                .Entity<VisitorWorkflowSettings>()
                .Property(x => x.WelcomeMessage)
                .HasMaxLength(240);
            ConfigureTenantScopedEntity<Permit>(modelBuilder);
            ConfigureTenantScopedEntity<Visit>(modelBuilder);
            ConfigureTenantScopedEntity<VisitCompanion>(modelBuilder);
            ConfigureTenantScopedEntity<PermitActivity>(modelBuilder);
            ConfigureTenantScopedEntity<AuditLog>(modelBuilder);
            ConfigureTenantScopedEntity<Department>(modelBuilder);
            ConfigureTenantScopedEntity<UserAccount>(modelBuilder);
            ConfigureTenantScopedEntity<UserActivity>(modelBuilder);
            ConfigureTenantScopedEntity<SessionRecord>(modelBuilder);
            ConfigureTenantScopedEntity<Delegation>(modelBuilder);
            ConfigureTenantScopedEntity<DelegationPermission>(modelBuilder);
            ConfigureTenantScopedEntity<AdministrationSettings>(modelBuilder);
            modelBuilder
                .Entity<VisitorWorkflowSettings>()
                .HasQueryFilter(settings => settings.TenantId == CurrentTenantId);
            ConfigureTenantScopedEntity<DisplayDevice>(modelBuilder);
            ConfigureTenantScopedEntity<DisplaySecuritySettings>(modelBuilder);
            ConfigureTenantScopedEntity<ExternalUserLogin>(modelBuilder);
            ConfigureTenantScopedEntity<EmailNotificationOutbox>(modelBuilder);
            ConfigureTenantScopedEntity<InAppNotification>(modelBuilder);
            ConfigureTenantScopedEntity<WorkplaceSite>(modelBuilder);
            ConfigureTenantScopedEntity<WorkplaceSiteEntrance>(modelBuilder);
            ConfigureTenantScopedEntity<PersonProfile>(modelBuilder);
            ConfigureTenantScopedEntity<PersonPhoto>(modelBuilder);
            ConfigureTenantScopedEntity<EmergencySession>(modelBuilder);
            ConfigureTenantScopedEntity<EmergencySessionMember>(modelBuilder);

            modelBuilder.Entity<WorkplaceSite>().Property(x => x.Name).HasMaxLength(128);
            modelBuilder.Entity<WorkplaceSite>().Property(x => x.Code).HasMaxLength(32);
            modelBuilder.Entity<WorkplaceSite>().Property(x => x.Address).HasMaxLength(256);
            modelBuilder.Entity<WorkplaceSite>().Property(x => x.Latitude).HasPrecision(10, 7);
            modelBuilder.Entity<WorkplaceSite>().Property(x => x.Longitude).HasPrecision(10, 7);
            modelBuilder.Entity<WorkplaceSite>().Property(x => x.PermitsEnabled).HasDefaultValue(true);
            modelBuilder.Entity<WorkplaceSite>().Property(x => x.VisitsEnabled).HasDefaultValue(true);
            modelBuilder.Entity<WorkplaceSite>().Property(x => x.SelfServiceEnabled).HasDefaultValue(true);
            modelBuilder.Entity<WorkplaceSite>().Property(x => x.QueueEnabled).HasDefaultValue(false);
            modelBuilder.Entity<WorkplaceSite>().Property(x => x.GateEnabled).HasDefaultValue(true);
            modelBuilder.Entity<WorkplaceSite>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            modelBuilder.Entity<WorkplaceSite>().HasIndex(x => new { x.TenantId, x.Code });

            modelBuilder.Entity<WorkplaceSiteEntrance>().Property(x => x.Name).HasMaxLength(128);
            modelBuilder.Entity<WorkplaceSiteEntrance>().Property(x => x.Code).HasMaxLength(32);
            modelBuilder
                .Entity<WorkplaceSiteEntrance>()
                .Property(x => x.LocationDescription)
                .HasMaxLength(256);
            modelBuilder
                .Entity<WorkplaceSiteEntrance>()
                .HasIndex(x => new { x.TenantId, x.WorkplaceSiteId, x.Name })
                .IsUnique();
            modelBuilder
                .Entity<WorkplaceSiteEntrance>()
                .HasIndex(x => new { x.TenantId, x.WorkplaceSiteId, x.Code });
            modelBuilder
                .Entity<WorkplaceSiteEntrance>()
                .HasOne(x => x.WorkplaceSite)
                .WithMany(x => x.Entrances)
                .HasForeignKey(x => x.WorkplaceSiteId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PersonProfile>().Property(x => x.SourceKey).HasMaxLength(160);
            modelBuilder.Entity<PersonProfile>().Property(x => x.PersonType).HasMaxLength(24);
            modelBuilder.Entity<PersonProfile>().Property(x => x.FullName).HasMaxLength(128);
            modelBuilder.Entity<PersonProfile>().Property(x => x.NationalId).HasMaxLength(32);
            modelBuilder.Entity<PersonProfile>().Property(x => x.PhoneNumber).HasMaxLength(32);
            modelBuilder.Entity<PersonProfile>().Property(x => x.Email).HasMaxLength(256);
            modelBuilder.Entity<PersonProfile>().Property(x => x.EmployeeNumber).HasMaxLength(64);
            modelBuilder.Entity<PersonProfile>().Property(x => x.Department).HasMaxLength(128);
            modelBuilder.Entity<PersonProfile>().Property(x => x.JobTitle).HasMaxLength(128);
            modelBuilder.Entity<PersonProfile>().Property(x => x.Organization).HasMaxLength(256);
            modelBuilder.Entity<PersonProfile>().Property(x => x.LastReference).HasMaxLength(64);
            modelBuilder.Entity<PersonProfile>()
                .HasIndex(x => new { x.TenantId, x.SourceKey })
                .IsUnique();
            modelBuilder.Entity<PersonProfile>().HasIndex(x => new { x.TenantId, x.PersonType });
            modelBuilder.Entity<PersonPhoto>().Property(x => x.ContentType).HasMaxLength(64);
            modelBuilder.Entity<PersonPhoto>()
                .HasOne<PersonProfile>()
                .WithOne()
                .HasForeignKey<PersonPhoto>(x => x.PersonProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EmergencySession>().Property(x => x.Status).HasMaxLength(24);
            modelBuilder.Entity<EmergencySession>().Property(x => x.StartedBy).HasMaxLength(64);
            modelBuilder.Entity<EmergencySession>().Property(x => x.EndedBy).HasMaxLength(64);
            modelBuilder.Entity<EmergencySession>().HasIndex(x => new { x.TenantId, x.WorkplaceSiteId, x.Status });
            modelBuilder.Entity<EmergencySession>().HasOne(x => x.WorkplaceSite).WithMany()
                .HasForeignKey(x => x.WorkplaceSiteId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<EmergencySessionMember>().Property(x => x.FullName).HasMaxLength(128);
            modelBuilder.Entity<EmergencySessionMember>().Property(x => x.PersonType).HasMaxLength(24);
            modelBuilder.Entity<EmergencySessionMember>().Property(x => x.Reference).HasMaxLength(64);
            modelBuilder.Entity<EmergencySessionMember>().Property(x => x.Location).HasMaxLength(256);
            modelBuilder.Entity<EmergencySessionMember>().Property(x => x.Status).HasMaxLength(24);
            modelBuilder.Entity<EmergencySessionMember>().Property(x => x.UpdatedBy).HasMaxLength(64);
            modelBuilder.Entity<EmergencySessionMember>().HasOne(x => x.EmergencySession)
                .WithMany(x => x.Members).HasForeignKey(x => x.EmergencySessionId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EmergencySessionMember>().HasIndex(x => new { x.TenantId, x.EmergencySessionId, x.Status });

            modelBuilder.Entity<Permit>().Property(x => x.PermitNumber).HasMaxLength(32);
            modelBuilder.Entity<Permit>().Property(x => x.PermitType).HasMaxLength(24);
            modelBuilder.Entity<Permit>().Property(x => x.AccessMode).HasMaxLength(24);
            modelBuilder.Entity<Permit>().Property(x => x.CurrentState).HasMaxLength(16);
            modelBuilder.Entity<Permit>().Property(x => x.VisitLocation).HasMaxLength(256);
            modelBuilder.Entity<Permit>().HasIndex(x => new { x.TenantId, x.WorkplaceSiteId });
            modelBuilder.Entity<Permit>().HasOne(x => x.WorkplaceSite).WithMany(x => x.Permits)
                .HasForeignKey(x => x.WorkplaceSiteId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Permit>().HasOne(x => x.WorkplaceSiteEntrance).WithMany()
                .HasForeignKey(x => x.WorkplaceSiteEntranceId).OnDelete(DeleteBehavior.Restrict);
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
            modelBuilder.Entity<Visit>().HasIndex(x => new { x.TenantId, x.WorkplaceSiteId });
            modelBuilder.Entity<Visit>().HasOne(x => x.WorkplaceSite).WithMany(x => x.Visits)
                .HasForeignKey(x => x.WorkplaceSiteId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Visit>().HasOne(x => x.WorkplaceSiteEntrance).WithMany()
                .HasForeignKey(x => x.WorkplaceSiteEntranceId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Visit>().HasOne(x => x.Department).WithMany()
                .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Visit>().Property(x => x.NationalId).HasMaxLength(32);
            modelBuilder.Entity<Visit>().Property(x => x.PhoneNumber).HasMaxLength(32);
            modelBuilder.Entity<Visit>().Property(x => x.VisitorEmail).HasMaxLength(256);
            modelBuilder.Entity<Visit>().Property(x => x.Purpose).HasMaxLength(256);
            modelBuilder.Entity<Visit>().Property(x => x.HostName).HasMaxLength(128);
            modelBuilder.Entity<Visit>().Property(x => x.VisitedPersonName).HasMaxLength(128);
            modelBuilder.Entity<Visit>().Property(x => x.VisitedPersonType).HasMaxLength(32);
            modelBuilder.Entity<Visit>().Property(x => x.ApprovalStatus).HasMaxLength(32);
            modelBuilder.Entity<Visit>().Property(x => x.ServiceDurationMinutes).HasDefaultValue(30);
            modelBuilder.Entity<Visit>().Property(x => x.ServiceOperatorUsername).HasMaxLength(64);
            modelBuilder.Entity<Visit>().Property(x => x.ServiceOperatorDisplayName).HasMaxLength(128);
            modelBuilder
                .Entity<Visit>()
                .Property(x => x.RequestSource)
                .HasMaxLength(32)
                .HasDefaultValue(Visit.RequestSourceInternal);
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
            modelBuilder.Entity<Department>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
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
            modelBuilder.Entity<UserAccount>().HasIndex(x => new { x.TenantId, x.WorkplaceSiteId });
            modelBuilder.Entity<UserAccount>()
                .HasOne(x => x.WorkplaceSite)
                .WithMany(x => x.UserAccounts)
                .HasForeignKey(x => x.WorkplaceSiteId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<UserAccount>().Property(x => x.Department).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.EmployeeNumber).HasMaxLength(64);
            modelBuilder.Entity<UserAccount>().Property(x => x.JobTitle).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.OperatorBadgeCode).HasMaxLength(64);
            modelBuilder.Entity<UserAccount>().Property(x => x.OperatorPinHash).HasMaxLength(256);
            modelBuilder.Entity<UserAccount>().Property(x => x.OperatorPinSalt).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.PhoneNumber).HasMaxLength(32);
            modelBuilder.Entity<UserAccount>().Property(x => x.Email).HasMaxLength(256);
            modelBuilder.Entity<UserAccount>().Property(x => x.IsEmailConfirmed).HasDefaultValue(true);
            modelBuilder.Entity<UserAccount>().Property(x => x.MfaEnabled).HasDefaultValue(false);
            modelBuilder.Entity<UserAccount>().Property(x => x.MfaSecretProtected).HasMaxLength(2048);
            modelBuilder.Entity<UserAccount>().Property(x => x.MfaRecoveryCodeHashesJson).HasMaxLength(4096);
            modelBuilder.Entity<UserAccount>().HasIndex(x => new { x.TenantId, x.Email });
            modelBuilder.Entity<UserAccount>().Property(x => x.Email).HasMaxLength(128);
            modelBuilder.Entity<UserAccount>().Property(x => x.ManagerUsername).HasMaxLength(64);
            modelBuilder.Entity<Permit>().Property(x => x.HolderEmail).HasMaxLength(256);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.NotificationType)
                .HasMaxLength(64);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.ReferenceType)
                .HasMaxLength(32);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.ReferenceId)
                .HasMaxLength(64);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.DeduplicationKey)
                .HasMaxLength(256);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.RecipientEmail)
                .HasMaxLength(256);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.RecipientName)
                .HasMaxLength(128);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.Subject)
                .HasMaxLength(256);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.Status)
                .HasMaxLength(24);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.LockToken)
                .HasMaxLength(64);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .Property(x => x.LastError)
                .HasMaxLength(1000);
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .HasIndex(x => new { x.TenantId, x.DeduplicationKey })
                .IsUnique();
            modelBuilder
                .Entity<EmailNotificationOutbox>()
                .HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            modelBuilder.Entity<InAppNotification>().Property(x => x.RecipientUsername).HasMaxLength(64);
            modelBuilder.Entity<InAppNotification>().Property(x => x.Category).HasMaxLength(32);
            modelBuilder.Entity<InAppNotification>().Property(x => x.Severity).HasMaxLength(16);
            modelBuilder.Entity<InAppNotification>().Property(x => x.Title).HasMaxLength(160);
            modelBuilder.Entity<InAppNotification>().Property(x => x.Message).HasMaxLength(500);
            modelBuilder.Entity<InAppNotification>().Property(x => x.ActionUrl).HasMaxLength(512);
            modelBuilder.Entity<InAppNotification>().Property(x => x.SourceKey).HasMaxLength(256);
            modelBuilder
                .Entity<InAppNotification>()
                .HasIndex(x => new { x.TenantId, x.RecipientUsername, x.SourceKey })
                .IsUnique();
            modelBuilder
                .Entity<InAppNotification>()
                .HasIndex(x => new { x.TenantId, x.RecipientUsername, x.DismissedAtUtc, x.OccurredAtUtc });
            modelBuilder.Entity<ExternalUserLogin>().Property(x => x.TenantId).HasMaxLength(64);
            modelBuilder.Entity<ExternalUserLogin>().Property(x => x.Username).HasMaxLength(64);
            modelBuilder.Entity<ExternalUserLogin>().Property(x => x.Provider).HasMaxLength(32);
            modelBuilder.Entity<ExternalUserLogin>().Property(x => x.Issuer).HasMaxLength(256);
            modelBuilder.Entity<ExternalUserLogin>().Property(x => x.Subject).HasMaxLength(256);
            modelBuilder.Entity<ExternalUserLogin>().Property(x => x.EmailAtLinkTime).HasMaxLength(256);
            modelBuilder
                .Entity<ExternalUserLogin>()
                .HasIndex(x => new { x.Provider, x.Issuer, x.Subject })
                .IsUnique();
            modelBuilder
                .Entity<ExternalUserLogin>()
                .HasIndex(x => new { x.TenantId, x.Username, x.Provider })
                .IsUnique();
            modelBuilder.Entity<LoginAttemptRecord>().Property(x => x.KeyHash).HasMaxLength(64);
            modelBuilder.Entity<SignupAttemptRecord>().Property(x => x.KeyHash).HasMaxLength(64);
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
            modelBuilder.Entity<DisplayDevice>().Property(x => x.Mode).HasMaxLength(24);
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
                .Entity<DisplayDevice>()
                .HasOne(x => x.WorkplaceSite)
                .WithMany(x => x.DisplayDevices)
                .HasForeignKey(x => x.WorkplaceSiteId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder
                .Entity<DisplayDevice>()
                .HasOne(x => x.WorkplaceSiteEntrance)
                .WithMany(x => x.DisplayDevices)
                .HasForeignKey(x => x.WorkplaceSiteEntranceId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder
                .Entity<DisplaySecuritySettings>()
                .Property(x => x.SetupKeyHash)
                .HasMaxLength(128);
        }

        private void ConfigureTenantScopedEntity<TEntity>(ModelBuilder modelBuilder)
            where TEntity : class, ITenantScopedEntity
        {
            modelBuilder
                .Entity<TEntity>()
                .Property(x => x.TenantId)
                .HasMaxLength(64)
                .HasDefaultValue(TenantDefaults.DefaultTenantId);
            modelBuilder.Entity<TEntity>().HasIndex(x => x.TenantId);
            modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.TenantId == CurrentTenantId);
        }

        public override int SaveChanges()
        {
            ApplyTenantId();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplyTenantId();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyTenantId();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        )
        {
            ApplyTenantId();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplyTenantId()
        {
            foreach (var entry in ChangeTracker.Entries<ITenantScopedEntity>())
            {
                if (entry.State != EntityState.Added)
                {
                    continue;
                }

                if (
                    string.IsNullOrWhiteSpace(entry.Entity.TenantId)
                    || string.Equals(
                        entry.Entity.TenantId,
                        TenantDefaults.DefaultTenantId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    entry.Entity.TenantId = CurrentTenantId;
                }
            }
        }
    }
}
