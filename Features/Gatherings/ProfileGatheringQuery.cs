using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public static class ProfileGatheringQuery
{
    public static IQueryable<GameGathering> AgendaCandidates(IQueryable<GameGathering> source, long participantId,
        IReadOnlyCollection<string> authorizedCommunityKeys, DateTimeOffset now)
    {
        var earliest = now.AddMinutes(-GatheringPlayTiming.MaximumEstimateMinutes - GatheringPlayTiming.ConfirmationGraceMinutes).AddDays(-1);
        return source.Where(x => authorizedCommunityKeys.Contains(x.CommunityKey) && x.Community.DeletedAt == null
            && x.Community.IsActive && x.StartsAtUtc >= earliest
            && (GatheringLifecycle.ScheduledStatuses.Contains(x.Status)
                || x.Status == GatheringStatus.Completed && x.ConfirmedWasPlayed == null)
            && (x.OrganizerParticipantId == participantId || x.Participants.Any(p => p.ParticipantId == participantId
                && (p.Status == GatheringParticipationStatus.Confirmed || p.Status == GatheringParticipationStatus.Waitlisted && x.StartsAtUtc > now))))
            .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id);
    }

    public static bool IsInAgenda(GameGathering gathering, DateTimeOffset now) =>
        GatheringLifecycle.IsUpcoming(gathering, now)
        || gathering.StartsAtUtc <= now && gathering.Status != GatheringStatus.Cancelled && gathering.ConfirmedWasPlayed == null
            && GatheringPlayTiming.ConfirmationDueAt(gathering).AddDays(1) > now;

    public static IQueryable<GameGathering> Apply(IQueryable<GameGathering> source, long participantId,
        IReadOnlyCollection<string> authorizedCommunityKeys, DateTimeOffset now) =>
        GatheringListQuery.Apply(source.Where(x => authorizedCommunityKeys.Contains(x.CommunityKey)
                && (x.OrganizerParticipantId == participantId
                    || x.Participants.Any(p => p.ParticipantId == participantId
                        && p.Status == GatheringParticipationStatus.Confirmed))),
            GatheringListScope.Upcoming, now);
}
