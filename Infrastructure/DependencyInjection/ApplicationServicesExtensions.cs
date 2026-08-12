using Microsoft.AspNetCore.Authentication;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Backup;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Delegations;
using VehiclePermitSystemWeb.Services.Display;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Monitoring;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Tenants;
using VehiclePermitSystemWeb.Services.Uploads;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;
using VehiclePermitSystemWeb.Services.Workplace;

namespace VehiclePermitSystemWeb.Infrastructure.DependencyInjection
{
    public static class ApplicationServicesExtensions
    {
        public static IServiceCollection AddVehiclePermitApplicationServices(
            this IServiceCollection services,
            bool runSchemaUpgradeOnly
        )
        {
            services.AddSingleton<IDatabaseBootstrapService, DatabaseBootstrapService>();
            services.AddSingleton<
                VehiclePermitSystemWeb.Services.Common.ISystemClock,
                VehiclePermitSystemWeb.Services.Common.SystemClock
            >();
            services.AddSingleton<IPermitMonitoringService, PermitMonitoringService>();
            services.AddSingleton<IPermitApprovalService, PermitApprovalService>();
            services.AddSingleton<IPermitLifecycleService, PermitLifecycleService>();
            services.AddSingleton<ILeavePolicyService, LeavePolicyService>();
            services.AddSingleton<IGatePolicyService, GatePolicyService>();
            services.AddSingleton<IPermitAuditService, PermitAuditService>();
            services.AddSingleton<IAuditLogService, AuditLogService>();
            services.AddSingleton<IDelegationService, DelegationService>();
            services.AddSingleton<IDisplayDeviceService, DisplayDeviceService>();
            services.AddSingleton<IMonitoringDashboardService, MonitoringDashboardService>();
            services.AddSingleton<IPermitMovementService, PermitMovementService>();
            services.AddSingleton<IReportsDashboardService, ReportsDashboardService>();
            services.AddSingleton<IReportsDocumentService, ReportsDocumentService>();
            services.AddSingleton<IAccessControlService, AccessControlService>();
            services.AddSingleton<IPermitService, PermitService>();
            services.AddSingleton<IVisitService, VisitService>();
            services.AddSingleton<IVisitorWorkflowService, VisitorWorkflowService>();
            services.AddSingleton<IUserAdminService, UserAdminService>();
            services.AddSingleton<IPlatformSettingsService, PlatformSettingsService>();
            services.AddSingleton<ISubscriptionPlanService, SubscriptionPlanService>();
            services.AddSingleton<LoginAttemptGuard>();
            services.AddSingleton<SignupAttemptGuard>();
            services.AddSingleton<IExternalLoginService, ExternalLoginService>();
            services.AddSingleton<
                IAccountEmailVerificationService,
                AccountEmailVerificationService
            >();
            services.AddSingleton<
                IAccountPasswordResetService,
                AccountPasswordResetService
            >();
            services.AddSingleton<ITenantContext, HttpTenantContext>();
            services.AddScoped<ITenantFeatureService, TenantFeatureService>();
            services.AddSingleton<ITenantManagementService, TenantManagementService>();
            services.AddSingleton<UserSessionService>();
            services.AddScoped<IToastNotificationService, ToastNotificationService>();
            services.AddSingleton<ISystemEmailSender, SmtpSystemEmailSender>();
            services.AddSingleton<
                IOperationalEmailNotificationQueue,
                OperationalEmailNotificationQueue
            >();
            services.AddSingleton<IEmailNotificationDispatcher, EmailNotificationDispatcher>();
            services.AddSingleton<INotificationCenterService, NotificationCenterService>();
            services.AddSingleton<IDataRetentionService, DataRetentionService>();
            services.AddTransient<IClaimsTransformation, DelegationClaimsTransformation>();
            services.AddSingleton<BackupService>();
            services.AddSingleton<IUploadThreatScanner, ClamAvUploadThreatScanner>();
            services.AddSingleton<IWorkplaceDirectoryService, WorkplaceDirectoryService>();
            services.AddSingleton<IEmergencyService, EmergencyService>();

            if (!runSchemaUpgradeOnly)
            {
                services.AddHostedService<AutomaticBackupHostedService>();
                services.AddHostedService<AutomaticWorkEndHostedService>();
                services.AddHostedService<EmailNotificationHostedService>();
                services.AddHostedService<DataRetentionHostedService>();
            }

            return services;
        }
    }
}
