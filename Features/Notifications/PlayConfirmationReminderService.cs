using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Features.Notifications;

public sealed class PlayConfirmationReminderService(AppDbContext db, NotificationService notifications, TimeProvider clock)
{
    public static bool NeedsConfirmation(GameGathering g) => g.Status == GatheringStatus.Completed
        && g.ConfirmedWasPlayed is null && g.Community.IsActive && g.Community.DeletedAt is null;

    public async Task EnqueueDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // Seven days is the largest supported estimate; one more day permits short restart recovery.
        var oldest = now.AddMinutes(-GatheringPlayTiming.MaximumEstimateMinutes
            - GatheringPlayTiming.ConfirmationGraceMinutes).AddDays(-1);
        var candidates = await db.GameGatherings.AsNoTracking().Include(g => g.OrganizerParticipant).Include(g => g.Community)
            .Where(g => g.Status == GatheringStatus.Completed && g.ConfirmedWasPlayed == null
                && g.StartsAtUtc <= now && g.StartsAtUtc >= oldest && g.Community.IsActive && g.Community.DeletedAt == null
                && !db.GatheringPlayRecords.Any(p => p.GatheringId == g.Id))
            .ToArrayAsync(ct);
        foreach (var gathering in candidates)
        {
            var due = GatheringPlayTiming.ConfirmationDueAt(gathering);
            if (due > now || now >= due.AddDays(1)) continue;
            await notifications.EnqueueAsync(new(gathering.OrganizerParticipant.TelegramUserId,
                NotificationKind.PlayConfirmationReminder, gathering.PublicId.ToString("N"),
                "Проверьте, состоялась ли игра, и отметьте результат в сборе.", gathering.CommunityKey, gathering.PublicId), ct);
        }
    }
}
