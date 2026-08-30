namespace oyinQ.Bot.Features.Collections;

public sealed class ClubMetadataRefreshWorker(IServiceScopeFactory scopeFactory,
    ILogger<ClubMetadataRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ClubMetadataRefreshService>();
                while (await service.ProcessOneAsync(stoppingToken)) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Club metadata refresh iteration failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
