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

    public static void RecalculateStatus(GameGathering gathering, bool preserveClosed = true)
    {
        if (gathering.Status is GatheringStatus.Completed or GatheringStatus.Cancelled
            || preserveClosed && gathering.Status == GatheringStatus.Closed)
            return;
        var occupied = OccupiedSeats(gathering);
        gathering.Status = occupied >= gathering.MaximumPlayers ? GatheringStatus.Full
            : occupied >= gathering.MinimumPlayers ? GatheringStatus.Ready : GatheringStatus.Recruiting;
    }
}
