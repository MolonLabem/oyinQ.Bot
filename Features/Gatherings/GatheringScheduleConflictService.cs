using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringPlanningOptions { public int ScheduleConflictWarningWindowMinutes { get; set; } = 120; }
public sealed record GatheringScheduleConflict(Guid PublicId, string GameName, DateTimeOffset StartsAtUtc, string CommunityKey, string Community, string TimeZoneId);
public sealed class GatheringScheduleConflictException(IReadOnlyList<GatheringScheduleConflict> conflicts)
    : InvalidOperationException("Возможное пересечение: у вас есть другой сбор с близким временем начала.")
{ public IReadOnlyList<GatheringScheduleConflict> Conflicts { get; } = conflicts; }

public sealed class GatheringScheduleConflictService(AppDbContext db, IOptions<GatheringPlanningOptions>? options = null)
{
    public async Task WarnAsync(long participantId, DateTimeOffset startsAt, Guid? exclude, bool confirmed, DateTimeOffset now, CancellationToken ct)
    {
        if (confirmed) return;
        var minutes = Math.Clamp(options?.Value.ScheduleConflictWarningWindowMinutes ?? 120, 0, 1440);
        if (minutes == 0) return;
        var lower = startsAt.AddMinutes(-minutes); var upper = startsAt.AddMinutes(minutes);
        var rows = await db.GameGatherings.AsNoTracking().Include(x => x.Community)
            .Where(x => x.PublicId != exclude && x.Community.DeletedAt == null && x.StartsAtUtc > now
                && x.StartsAtUtc >= lower && x.StartsAtUtc <= upper && GatheringLifecycle.ScheduledStatuses.Contains(x.Status)
                && (x.OrganizerParticipantId == participantId || x.Participants.Any(p => p.ParticipantId == participantId
                    && p.Status == GatheringParticipationStatus.Confirmed)))
            .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).Take(10).ToArrayAsync(ct);
        if (rows.Length > 0) throw new GatheringScheduleConflictException(rows.Select(x => new GatheringScheduleConflict(x.PublicId,
            GatheringGameSnapshotSerializer.Deserialize(x.GameSnapshotJson).Name, x.StartsAtUtc, x.CommunityKey, x.Community.Name, x.Community.TimeZoneId)).ToArray());
    }
}
