using oyinQ.Bot.Data.Entities;
using System.Linq.Expressions;

namespace oyinQ.Bot.Features.Gatherings;

public static class GatheringCapacity
{
    private static readonly Expression<Func<GameGathering, int>> OccupiedSeatsQuery = gathering =>
        1 + gathering.Participants.Count(x => x.Status == GatheringParticipationStatus.Confirmed) + gathering.Guests.Count;
    private static readonly Func<GameGathering, int> CountOccupiedSeats = OccupiedSeatsQuery.Compile();
    public static int OccupiedSeats(GameGathering gathering) => CountOccupiedSeats(gathering);

    // Build SQL predicates from the same count used by mutations and presentation.
    public static IQueryable<GameGathering> FilterSeats(IQueryable<GameGathering> query, bool available)
    {
        var parameter = OccupiedSeatsQuery.Parameters[0];
        var maximum = Expression.Property(parameter, nameof(GameGathering.MaximumPlayers));
        Expression condition = available ? Expression.LessThan(OccupiedSeatsQuery.Body, maximum)
            : Expression.GreaterThanOrEqual(OccupiedSeatsQuery.Body, maximum);
        if (available) condition = Expression.AndAlso(condition,
            Expression.NotEqual(Expression.Property(parameter, nameof(GameGathering.Status)), Expression.Constant(GatheringStatus.Closed)));
        return query.Where(Expression.Lambda<Func<GameGathering, bool>>(condition, parameter));
    }

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
