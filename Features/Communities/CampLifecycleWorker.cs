using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Communities;

public sealed class CampLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<CampLifecycleWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                while (await ProcessOneAsync(stoppingToken)) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Camp lifecycle worker iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();
        var candidates = await dbContext.Camps.AsNoTracking().Include(x => x.BotChat)
            .Where(x => x.Status == CampStatus.Active && x.EndDate != null)
            .OrderBy(x => x.EndDate).Select(x => new { x.Id, Camp = x, x.BotChat.TimeZoneId })
            .ToArrayAsync(cancellationToken);
        var candidate = candidates.FirstOrDefault(x => CampParticipationPolicy.HasEnded(x.Camp,
            x.TimeZoneId, now));
        if (candidate is null) return false;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var camp = await dbContext.Camps
            .FromSqlInterpolated($"SELECT * FROM \"Camps\" WHERE \"Id\" = {candidate.Id} FOR UPDATE")
            .Include(x => x.BotChat).SingleAsync(cancellationToken);
        if (camp.Status != CampStatus.Active
            || !CampParticipationPolicy.HasEnded(camp, camp.BotChat.TimeZoneId, now))
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        var activeStatuses = new[] { GatheringStatus.Recruiting, GatheringStatus.Ready,
            GatheringStatus.Full, GatheringStatus.Closed };
        var future = await dbContext.GameGatherings.AsNoTracking().CountAsync(x =>
            x.CommunityKey == camp.BotChatKey && x.StartsAtUtc > now && activeStatuses.Contains(x.Status),
            cancellationToken);
        if (future > 0)
        {
            logger.LogWarning("Expired Camp {CampId} cannot close because {GatheringCount} future gatherings remain.",
                camp.Id, future);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        camp.Status = CampStatus.Closed;
        camp.BotChat.IsActive = false;
        camp.UpdatedAt = camp.BotChat.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
