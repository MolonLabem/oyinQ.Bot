using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Features.Notifications;

public sealed class NotificationWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), time);
        do
        {
            try { await RunIterationAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogError(e, "Notification worker iteration failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunIterationAsync(CancellationToken ct)
    {
        await RunStageAsync<GatheringReminderService>((service, token) => service.EnqueueDueAsync(token), ct);
        await RunStageAsync<ProviderAttentionService>((service, token) => service.EnqueueDueAsync(token), ct);
        await RunStageAsync<PlayConfirmationReminderService>((service, token) => service.EnqueueDueAsync(token), ct);
        await RunStageAsync<NotificationDispatcher>(async (delivery, token) =>
        {
            for (var i = 0; i < 100 && await delivery.ProcessOneAsync(token); i++) { }
        }, ct);
    }

    private async Task RunStageAsync<T>(Func<T, CancellationToken, Task> run, CancellationToken ct) where T : notnull
    {
        // Each stage owns its context: a failed planner must neither block delivery nor leave tracked changes for it.
        try
        {
            ct.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            await run(scope.ServiceProvider.GetRequiredService<T>(), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { logger.LogError(e, "Notification stage {Stage} failed", typeof(T).Name); }
    }
}

public sealed class GatheringReminderService(AppDbContext db, NotificationService notifications, TimeProvider time)
{
    public async Task EnqueueDueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var gatherings = await db.GameGatherings.AsNoTracking().Include(x => x.OrganizerParticipant)
            .Include(x => x.Participants).ThenInclude(x => x.Participant)
            .Where(x => x.StartsAtUtc > now && x.StartsAtUtc <= now.AddHours(24)
                && GatheringLifecycle.ScheduledStatuses.Contains(x.Status)).ToArrayAsync(ct);
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(x => x.ReminderLeadMinutes > 0).ToDictionaryAsync(x => x.ParticipantId, ct);
        foreach (var g in gatherings)
            foreach (var p in g.Participants.Where(x => x.Status == GatheringParticipationStatus.Confirmed).Select(x => x.Participant)
                .Append(g.OrganizerParticipant).DistinctBy(x => x.Id))
                if (preferences.TryGetValue(p.Id, out var pref) && g.StartsAtUtc.AddMinutes(-pref.ReminderLeadMinutes) <= now)
                    await notifications.EnqueueAsync(new(p.TelegramUserId, NotificationKind.Reminder, g.PublicId.ToString("N"),
                        "Скоро начнётся ваш сбор.", g.CommunityKey, g.PublicId), ct);
    }
}
