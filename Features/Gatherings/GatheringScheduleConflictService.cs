using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringPlanningOptions { public int ScheduleConflictWarningWindowMinutes { get; set; } = 120; }
public sealed record GatheringScheduleConflict(Guid PublicId, string GameName, DateTimeOffset StartsAtUtc, string CommunityKey, string Community, string TimeZoneId, DateTimeOffset? EstimatedEndsAtUtc = null);
public sealed class GatheringScheduleConflictException(IReadOnlyList<GatheringScheduleConflict> conflicts)
    : InvalidOperationException("Возможное пересечение с другим сбором по ожидаемой длительности игры.")
{ public IReadOnlyList<GatheringScheduleConflict> Conflicts { get; } = conflicts; }

public sealed class GatheringScheduleConflictService(AppDbContext db, IOptions<GatheringPlanningOptions>? options = null, CommunityContextResolver? resolver = null)
{
    public async Task WarnAsync(long participantId, DateTimeOffset startsAt, Guid? exclude, bool confirmed, DateTimeOffset now, CancellationToken ct,
        GatheringGameSnapshot? game = null)
    {
        if (confirmed) return;
        var minutes = Math.Clamp(options?.Value.ScheduleConflictWarningWindowMinutes ?? 120, 0, 1440);
        if (minutes == 0) return;
        var upper = startsAt.AddMinutes(game is null ? GatheringPlayTiming.FallbackMinutes : GatheringPlayTiming.EstimateMinutes(game));
        var lower = startsAt.AddMinutes(-GatheringPlayTiming.MaximumEstimateMinutes);
        var query = db.GameGatherings.AsNoTracking().Include(x => x.Community)
            .Where(x => x.PublicId != exclude && x.Community.DeletedAt == null && x.Community.IsActive
                && x.StartsAtUtc >= lower && x.StartsAtUtc < upper
                && (GatheringLifecycle.ScheduledStatuses.Contains(x.Status) || x.Status == GatheringStatus.Completed && x.ConfirmedWasPlayed == null)
                && (x.OrganizerParticipantId == participantId || x.Participants.Any(p => p.ParticipantId == participantId
                    && p.Status == GatheringParticipationStatus.Confirmed)));
        var rows = await query.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).ToArrayAsync(ct);
        var overlapping = rows.Where(x => GatheringPlayTiming.EstimatedEnd(x) > now
            && GatheringPlayTiming.Overlaps(startsAt, upper, x.StartsAtUtc, GatheringPlayTiming.EstimatedEnd(x))).ToArray();
        if (resolver is not null && overlapping.Length > 0)
        {
            var telegramUserId = await db.Participants.Where(x => x.Id == participantId).Select(x => x.TelegramUserId).SingleAsync(ct);
            var authorized = new HashSet<string>();
            foreach (var key in overlapping.Select(x => x.CommunityKey).Distinct())
                if (await resolver.ResolveAuthorizedAsync(key, telegramUserId, ct) is not null) authorized.Add(key);
            overlapping = overlapping.Where(x => authorized.Contains(x.CommunityKey)).ToArray();
        }
        overlapping = overlapping.Take(10).ToArray();
        if (overlapping.Length > 0) throw new GatheringScheduleConflictException(overlapping.Select(x => new GatheringScheduleConflict(x.PublicId,
            GatheringGameSnapshotSerializer.Deserialize(x.GameSnapshotJson).Name, x.StartsAtUtc, x.CommunityKey, x.Community.Name, x.Community.TimeZoneId,
            GatheringPlayTiming.EstimatedEnd(x))).ToArray());
    }
}
