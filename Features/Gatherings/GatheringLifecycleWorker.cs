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
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        } while (!stoppingToken.IsCancellationRequested);
    }

    internal async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();
        var scheduledStatusValues = GatheringLifecycle.ScheduledStatuses.Select(x => (int)x).ToArray();
        Guid? changedPublicId = null;
        UnderfilledGatheringNotification? underfilled = null;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var gathering = await dbContext.GameGatherings
                .FromSqlInterpolated($$"""
                    SELECT * FROM "GameGatherings"
                    WHERE "StartsAtUtc" <= {{now}}
                      AND "Status" = ANY ({{scheduledStatusValues}})
                    ORDER BY "StartsAtUtc", "Id"
                    FOR UPDATE SKIP LOCKED LIMIT 1
                    """)
                .Include(x => x.OrganizerParticipant)
                .Include(x => x.Participants).ThenInclude(x => x.Participant)
                .Include(x => x.Guests)
                .AsSplitQuery()
                .SingleOrDefaultAsync(cancellationToken);
            if (gathering is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            var outcome = GatheringLifecycle.ApplyDue(gathering, now);
            if (outcome == GatheringLifecycleOutcome.Completed)
            {
                changedPublicId = gathering.PublicId;
            }
            else if (outcome == GatheringLifecycleOutcome.Cancelled)
            {
                underfilled = GatheringNotificationService.CaptureUnderfilled(gathering);
                changedPublicId = gathering.PublicId;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (underfilled is not null) await scope.ServiceProvider.GetRequiredService<GatheringNotificationService>().NotifyUnderfilledAsync(underfilled, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (changedPublicId is { } publicId)
        {
            var publication = scope.ServiceProvider.GetRequiredService<GatheringPublicationService>();
            await publication.PublishAsync(publicId, cancellationToken);
        }
        return true;
    }
}
