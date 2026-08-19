namespace oyinQ.Bot.Integrations.Tesera;

public sealed class TeseraAvailabilityMonitorService(
    TeseraAvailabilityService availabilityService,
    ILogger<TeseraAvailabilityMonitorService> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await availabilityService.GetAsync(
                    forceRefresh: false,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Tesera availability monitor failed.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
