using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<GatheringLifecycleWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                while (await ProcessOneAsync(stoppingToken)) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Gathering lifecycle worker iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();
        var recruiting = GatheringStatus.Recruiting;
        var ready = GatheringStatus.Ready;
        var full = GatheringStatus.Full;
        var closed = GatheringStatus.Closed;
        Guid? completedPublicId = null;
        UnderfilledGatheringNotification? underfilled = null;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var gathering = await dbContext.GameGatherings
                .FromSqlInterpolated($$"""
                    SELECT * FROM "GameGatherings"
                    WHERE "StartsAtUtc" <= {{now}}
                      AND "Status" IN ({{recruiting}}, {{ready}}, {{full}}, {{closed}})
                    ORDER BY "StartsAtUtc", "Id"
                    FOR UPDATE SKIP LOCKED LIMIT 1
                    """)
                .Include(x => x.OrganizerParticipant)
                .Include(x => x.Participants).ThenInclude(x => x.Participant)
                .SingleOrDefaultAsync(cancellationToken);
            if (gathering is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            var outcome = GatheringLifecycle.ApplyDue(gathering, now);
            if (outcome == GatheringLifecycleOutcome.Completed)
            {
                completedPublicId = gathering.PublicId;
            }
            else if (outcome == GatheringLifecycleOutcome.Delete)
            {
                underfilled = GatheringNotificationService.CaptureUnderfilled(gathering);
                var cleanup = GatheringLifecycle.CreateCleanup(gathering, now);
                if (cleanup is not null) dbContext.TelegramMessageCleanups.Add(cleanup);
                dbContext.GameGatherings.Remove(gathering);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (completedPublicId is { } publicId)
        {
            var publication = scope.ServiceProvider.GetRequiredService<GatheringPublicationService>();
            await publication.PublishAsync(publicId, cancellationToken);
        }
        if (underfilled is not null)
        {
            var notifications = scope.ServiceProvider.GetRequiredService<GatheringNotificationService>();
            await notifications.NotifyUnderfilledAsync(underfilled, cancellationToken);
        }
        return true;
    }
}
