using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringPublicationWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<GatheringPublicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), time);
        do
        {
            try { await RunIterationAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogError(e, "Gathering publication recovery failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunIterationAsync(CancellationToken ct)
    {
        Guid[] ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var now = time.GetUtcNow();
            ids = await db.GameGatherings.AsNoTracking().Where(x => x.PublicationStatus == GatheringPublicationStatus.Pending
                || (x.PublicationStatus == GatheringPublicationStatus.Preparing || x.PublicationStatus == GatheringPublicationStatus.Delivering)
                    && (x.PublicationLeaseExpiresAt == null || x.PublicationLeaseExpiresAt <= now))
                .OrderBy(x => x.LastPublicationAttemptAt).ThenBy(x => x.Id).Select(x => x.PublicId).Take(100).ToArrayAsync(ct);
        }
        foreach (var id in ids)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<GatheringPublicationService>().PublishAsync(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { logger.LogWarning(e, "Gathering {GatheringPublicId} publication recovery failed", id); }
        }
    }
}
