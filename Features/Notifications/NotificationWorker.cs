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
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<GatheringReminderService>().EnqueueDueAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<ProviderAttentionService>().EnqueueDueAsync(stoppingToken);
                var delivery = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                for (var i = 0; i < 100 && await delivery.ProcessOneAsync(stoppingToken); i++) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogError(e, "Notification worker iteration failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
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
