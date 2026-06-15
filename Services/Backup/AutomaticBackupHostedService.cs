namespace VehiclePermitSystemWeb.Services.Backup
{
    public class AutomaticBackupHostedService : BackgroundService
    {
        private readonly BackupService _backupService;
        private readonly ILogger<AutomaticBackupHostedService> _logger;

        public AutomaticBackupHostedService(
            BackupService backupService,
            ILogger<AutomaticBackupHostedService> logger
        )
        {
            _backupService = backupService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _backupService.EnsureDailyBackupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial automatic backup check failed.");
            }

            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _backupService.EnsureDailyBackupAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Automatic backup check failed.");
                }
            }
        }
    }
}
