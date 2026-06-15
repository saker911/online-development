using Microsoft.Extensions.DependencyInjection;

namespace VehiclePermitSystemWeb.Services.Gate
{
    public sealed class AutomaticWorkEndHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutomaticWorkEndHostedService> _logger;

        public AutomaticWorkEndHostedService(
            IServiceProvider serviceProvider,
            ILogger<AutomaticWorkEndHostedService> logger
        )
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RunWorkEndClosureAsync(stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunWorkEndClosureAsync(stoppingToken);
            }
        }

        private async Task RunWorkEndClosureAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var permitService = scope.ServiceProvider.GetRequiredService<IPermitService>();
                var closedCount = permitService.ClosePermitsAtWorkEnd("system");
                if (closedCount > 0)
                {
                    _logger.LogInformation(
                        "Closed {ClosedCount} permit(s) at work end.",
                        closedCount
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Automatic work end closure check failed.");
            }

            await Task.CompletedTask;
        }
    }
}
