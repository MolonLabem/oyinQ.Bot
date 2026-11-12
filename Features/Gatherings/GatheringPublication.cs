using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public static class GatheringPublication
{
    // Call after saving a provider mutation, inside its Camp-locked transaction.
    // Reuse the publication revision so preparation cannot publish stale box availability.
    public static async Task RequestForCampBoxesAsync(AppDbContext db, long campId,
        IReadOnlyCollection<long> bggIds, DateTimeOffset now, CancellationToken ct)
    {
        if (bggIds.Count == 0) return;
        var key = await db.Camps.Where(x => x.Id == campId).Select(x => x.BotChatKey).SingleAsync(ct);
        var upcoming = db.GameGatherings.Where(x => x.CommunityKey == key && x.StartsAtUtc > now
            && GatheringLifecycle.ScheduledStatuses.Contains(x.Status)
            && x.PublicationStatus != GatheringPublicationStatus.NotRequired);
        var candidates = await upcoming.AsNoTracking().Select(x => new { x.Id, x.GameSnapshotJson }).ToArrayAsync(ct);
        var ids = candidates.Where(x => GatheringGameSnapshotSerializer.Deserialize(x.GameSnapshotJson).BggId is { } bggId
                && bggIds.Contains(bggId))
            .Select(x => x.Id).ToArray();
        if (ids.Length == 0) return;
        var affected = upcoming.Where(x => ids.Contains(x.Id));
        if (!db.Database.IsRelational())
        {
            foreach (var gathering in await affected.ToArrayAsync(ct)) Request(gathering);
            await db.SaveChangesAsync(ct);
            return;
        }
        await affected.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.PublicationRevision, x => x.PublicationRevision + 1)
            .SetProperty(x => x.PublicationStatus, x =>
                x.PublicationStatus == GatheringPublicationStatus.Preparing
                || x.PublicationStatus == GatheringPublicationStatus.Delivering
                || x.PublicationStatus == GatheringPublicationStatus.DeliveryUnknown
                    ? x.PublicationStatus : GatheringPublicationStatus.Pending), ct);
        // A caller may already track a gathering. Keep only these saved fields in sync;
        // do not reload unrelated state or let a later SaveChanges overwrite the revision.
        var saved = await db.GameGatherings.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicationRevision, x.PublicationStatus }).ToDictionaryAsync(x => x.Id, ct);
        foreach (var entry in db.ChangeTracker.Entries<GameGathering>().Where(x => saved.ContainsKey(x.Entity.Id)))
        {
            var value = saved[entry.Entity.Id];
            entry.Property(x => x.PublicationRevision).CurrentValue = value.PublicationRevision;
            entry.Property(x => x.PublicationRevision).OriginalValue = value.PublicationRevision;
            entry.Property(x => x.PublicationStatus).CurrentValue = value.PublicationStatus;
            entry.Property(x => x.PublicationStatus).OriginalValue = value.PublicationStatus;
        }
    }

    public static void Request(GameGathering gathering)
    {
        gathering.PublicationRevision++;
        // A mutation must not erase a send in progress or permit reposting an unknown new message.
        if (gathering.PublicationStatus is not (GatheringPublicationStatus.Preparing or GatheringPublicationStatus.Delivering or GatheringPublicationStatus.DeliveryUnknown))
            gathering.PublicationStatus = GatheringPublicationStatus.Pending;
    }
}
