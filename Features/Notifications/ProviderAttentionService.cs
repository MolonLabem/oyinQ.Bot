using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Features.Notifications;

public sealed class ProviderAttentionService(AppDbContext db, GameProviderService providers, NotificationService notifications, TimeProvider clock)
{
    public async Task EnqueueDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.GameGatherings.AsNoTracking().Include(x => x.OrganizerParticipant)
            .Where(x => x.StartsAtUtc > now && x.StartsAtUtc <= now.AddHours(2) && x.Community.DeletedAt == null
                && GatheringLifecycle.ScheduledStatuses.Contains(x.Status)
                && db.NotificationPreferences.Any(p => p.ParticipantId == x.OrganizerParticipantId && p.OrganizerMissingProvider))
            .OrderBy(x => x.StartsAtUtc).ToArrayAsync(ct);
        foreach (var g in rows)
        {
            if ((await providers.ForGatheringAsync(g, g.OrganizerParticipantId, ct)).IsConfirmed) continue;
            await notifications.EnqueueAsync(new(g.OrganizerParticipant.TelegramUserId, NotificationKind.OrganizerMissingProvider,
                g.PublicId.ToString("N"), $"До сбора «{GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson).Name}» осталось меньше двух часов. Никто пока не подтвердил коробку.",
                g.CommunityKey, g.PublicId), ct);
        }
    }
}
