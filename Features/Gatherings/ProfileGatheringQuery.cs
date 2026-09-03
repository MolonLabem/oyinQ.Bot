using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public static class ProfileGatheringQuery
{
    public static IQueryable<GameGathering> Apply(IQueryable<GameGathering> source, long participantId,
        IReadOnlyCollection<string> authorizedCommunityKeys, DateTimeOffset now) =>
        GatheringListQuery.Apply(source.Where(x => authorizedCommunityKeys.Contains(x.CommunityKey)
                && (x.OrganizerParticipantId == participantId
                    || x.Participants.Any(p => p.ParticipantId == participantId
                        && p.Status == GatheringParticipationStatus.Confirmed))),
            GatheringListView.Upcoming, GatheringHistoryFilter.All, now);
}
