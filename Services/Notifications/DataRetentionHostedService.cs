namespace VehiclePermitSystemWeb.Services.Notifications
{
    public sealed class DataRetentionHostedService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
        private readonly IDataRetentionService _retentionService;
        private readonly ILogger<DataRetentionHostedService> _logger;

        public DataRetentionHostedService(
            IDataRetentionService retentionService,
            ILogger<DataRetentionHostedService> logger
        )
        {
            _retentionService = retentionService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);

            do
            {
                try
                {
                    var result = await _retentionService.RunAsync(stoppingToken);
                    _logger.LogInformation(
                        "Data retention completed for {TenantCount} tenants: {Notifications} notifications, {EmailRecords} email records, {AuditLogs} audit logs removed, {PersonalDataFields} personal-data fields sanitized.",
                        result.TenantCount,
                        result.NotificationsDeleted,
                        result.EmailRecordsDeleted,
                        result.AuditLogsDeleted,
                        result.PersonalDataFieldsSanitized
                    );
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Data retention execution failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
