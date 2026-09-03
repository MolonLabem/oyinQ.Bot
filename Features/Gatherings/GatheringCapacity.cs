using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public static class GatheringCapacity
{
    public static int OccupiedSeats(GameGathering gathering) =>
        1 + gathering.Participants.Count(x => x.Status == GatheringParticipationStatus.Confirmed)
          + gathering.Guests.Count;

    public static bool HasAvailableSeat(GameGathering gathering) =>
        OccupiedSeats(gathering) < gathering.MaximumPlayers;

    public static GameGatheringParticipant? PromoteFirstWaitlisted(GameGathering gathering)
    {
        if (!HasAvailableSeat(gathering)) return null;
        var promoted = gathering.Participants
            .Where(x => x.Status == GatheringParticipationStatus.Waitlisted)
            .OrderBy(x => x.JoinedAt).ThenBy(x => x.Id).FirstOrDefault();
        if (promoted is not null) promoted.Status = GatheringParticipationStatus.Confirmed;
        return promoted;
    }

    public static IReadOnlyList<GameGatheringParticipant> PromoteWaitlistedToCapacity(
        GameGathering gathering)
    {
        var promoted = new List<GameGatheringParticipant>();
        while (HasAvailableSeat(gathering) && PromoteFirstWaitlisted(gathering) is { } participant)
            promoted.Add(participant);
        return promoted;
    }

    public static GatheringStatus CalculateOpenStatus(GameGathering gathering)
    {
        var occupied = OccupiedSeats(gathering);
        return occupied >= gathering.MaximumPlayers ? GatheringStatus.Full
            : occupied >= gathering.MinimumPlayers ? GatheringStatus.Ready : GatheringStatus.Recruiting;
    }

    public static void SynchronizeScheduledStatus(GameGathering gathering)
    {
        if (gathering.Status == GatheringStatus.Closed || GatheringLifecycle.IsTerminal(gathering.Status)) return;
        gathering.Status = CalculateOpenStatus(gathering);
    }
}
