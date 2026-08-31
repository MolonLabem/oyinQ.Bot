namespace oyinQ.Bot.Features.Collections;

public sealed class ClubBggImportWorker(IServiceScopeFactory scopeFactory,
    ILogger<ClubBggImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ClubBggImportService>();
                while (await service.ProcessOneAsync(stoppingToken)) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Club BGG import worker iteration failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
