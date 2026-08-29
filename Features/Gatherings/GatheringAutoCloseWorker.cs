using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringAutoCloseWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<GatheringAutoCloseWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), timeProvider);
        do
        {
            try
            {
                while (await CloseOneAsync(stoppingToken)) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Gathering auto-close worker iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> CloseOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();
        var recruiting = GatheringStatus.Recruiting;
        var ready = GatheringStatus.Ready;
        var full = GatheringStatus.Full;
        Guid publicId;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var gathering = await dbContext.GameGatherings
                .FromSqlInterpolated($$"""
                    SELECT * FROM "GameGatherings"
                    WHERE "StartsAtUtc" <= {{now}}
                      AND "Status" IN ({{recruiting}}, {{ready}}, {{full}})
                    ORDER BY "StartsAtUtc", "Id"
                    FOR UPDATE SKIP LOCKED LIMIT 1
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (gathering is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            GatheringRules.Close(gathering, now);
            gathering.PublicationStatus = GatheringPublicationStatus.Pending;
            publicId = gathering.PublicId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var publication = scope.ServiceProvider.GetRequiredService<GatheringPublicationService>();
        await publication.PublishAsync(publicId, cancellationToken);
        return true;
    }
}
