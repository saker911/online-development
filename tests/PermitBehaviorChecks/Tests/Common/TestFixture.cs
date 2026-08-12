using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Backup;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Delegations;
using VehiclePermitSystemWeb.Services.Display;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Monitoring;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;
using VehiclePermitSystemWeb.Services.Workplace;

namespace PermitBehaviorChecks;

internal sealed class TestFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public TestFixture()
    {
        Clock = new MutableSystemClock(new DateTime(2026, 4, 13, 9, 0, 0));
        AppClock.Configure(Clock);

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton<ISystemClock>(Clock);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<ITenantContext, DefaultTenantContext>();
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseSqlite(_connection)
        );
        services.AddSingleton<IPermitAuditService, PermitAuditService>();
        services.AddSingleton<IDelegationService, DelegationService>();
        services.AddSingleton<IDisplayDeviceService, DisplayDeviceService>();
        services.AddSingleton<ILeavePolicyService, LeavePolicyService>();
        services.AddSingleton<IGatePolicyService, GatePolicyService>();
        services.AddSingleton<IPermitMonitoringService, PermitMonitoringService>();
        services.AddSingleton<IPermitApprovalService, PermitApprovalService>();
        services.AddSingleton<IPermitLifecycleService, PermitLifecycleService>();
        services.AddSingleton<IPermitMovementService, PermitMovementService>();
        services.AddSingleton<IAccessControlService, AccessControlService>();
        services.AddSingleton<IAuditLogService, AuditLogService>();
        services.AddSingleton<IPermitService, PermitService>();
        services.AddSingleton<UserSessionService>();
        services.AddSingleton<IUserAdminService, UserAdminService>();
        services.AddSingleton<IVisitService, VisitService>();
        services.AddSingleton<
            IOperationalEmailNotificationQueue,
            OperationalEmailNotificationQueue
        >();
        services.AddSingleton<IReportsDashboardService, ReportsDashboardService>();
        services.AddSingleton<IMonitoringDashboardService, MonitoringDashboardService>();
        services.AddSingleton<INotificationCenterService, NotificationCenterService>();
        services.AddSingleton<IDataRetentionService, DataRetentionService>();
        services.AddSingleton<IWorkplaceDirectoryService, WorkplaceDirectoryService>();
        services.AddSingleton<IEmergencyService, EmergencyService>();

        _serviceProvider = services.BuildServiceProvider();

        DbFactory = _serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        ServiceProvider = _serviceProvider;
        PermitService = _serviceProvider.GetRequiredService<IPermitService>();
        UserAdminService = _serviceProvider.GetRequiredService<IUserAdminService>();
        VisitService = _serviceProvider.GetRequiredService<IVisitService>();
        ReportsDashboardService = _serviceProvider.GetRequiredService<IReportsDashboardService>();
        MonitoringDashboardService =
            _serviceProvider.GetRequiredService<IMonitoringDashboardService>();
        AccessControlService = _serviceProvider.GetRequiredService<IAccessControlService>();
        DelegationService = _serviceProvider.GetRequiredService<IDelegationService>();
        DisplayDeviceService = _serviceProvider.GetRequiredService<IDisplayDeviceService>();
        DataRetentionService = _serviceProvider.GetRequiredService<IDataRetentionService>();
        NotificationCenterService = _serviceProvider.GetRequiredService<INotificationCenterService>();
        WorkplaceDirectoryService =
            _serviceProvider.GetRequiredService<IWorkplaceDirectoryService>();
        EmergencyService = _serviceProvider.GetRequiredService<IEmergencyService>();

        using var db = DbFactory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Tenants.Add(
            new Tenant
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Name = TenantDefaults.DefaultTenantName,
                Slug = TenantDefaults.DefaultTenantId,
                IsActive = true,
                SubscriptionStatus = TenantSubscriptionStatuses.Active,
            }
        );
        db.UserAccounts.Add(
            new UserAccount
            {
                Username = "tester",
                DisplayName = "Tester",
                FullName = "Tester",
                Department = "الإدارة العامة",
                JobTitle = "مدير عام",
                PhoneNumber = "0500000000",
                Role = AppRoles.GeneralManager,
                IsActive = true,
                CanViewPermits = true,
                CanApprovePermit = true,
                CanViewVisits = true,
                CanApproveVisits = true,
                CanScanOperations = true,
                CanManageDelegations = true,
            }
        );
        db.SaveChanges();
    }

    public MutableSystemClock Clock { get; }

    public IServiceProvider ServiceProvider { get; }

    public IDbContextFactory<ApplicationDbContext> DbFactory { get; }

    public IPermitService PermitService { get; }

    public IUserAdminService UserAdminService { get; }

    public IVisitService VisitService { get; }

    public IReportsDashboardService ReportsDashboardService { get; }

    public IMonitoringDashboardService MonitoringDashboardService { get; }

    public IAccessControlService AccessControlService { get; }

    public IDelegationService DelegationService { get; }

    public IDisplayDeviceService DisplayDeviceService { get; }

    public IDataRetentionService DataRetentionService { get; }

    public INotificationCenterService NotificationCenterService { get; }

    public IWorkplaceDirectoryService WorkplaceDirectoryService { get; }

    public IEmergencyService EmergencyService { get; }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
