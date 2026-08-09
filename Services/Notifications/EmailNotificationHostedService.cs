namespace VehiclePermitSystemWeb.Services.Notifications
{
    public sealed class EmailNotificationHostedService : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
        private readonly IEmailNotificationDispatcher _dispatcher;
        private readonly ILogger<EmailNotificationHostedService> _logger;

        public EmailNotificationHostedService(
            IEmailNotificationDispatcher dispatcher,
            ILogger<EmailNotificationHostedService> logger
        )
        {
            _dispatcher = dispatcher;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _dispatcher.ProcessPendingAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Unexpected failure while processing the email notification outbox."
                    );
                }

                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }
}
