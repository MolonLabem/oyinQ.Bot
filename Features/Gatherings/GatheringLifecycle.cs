using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public enum GatheringLifecycleOutcome
{
    None,
    Completed,
    Cancelled
}

public static class GatheringLifecycle
{
    public const string InsufficientParticipantsReason = "Не набралось достаточно участников";
    internal static readonly GatheringStatus[] ScheduledStatuses =
        [GatheringStatus.Recruiting, GatheringStatus.Ready, GatheringStatus.Full, GatheringStatus.Closed];

    public static bool IsScheduled(GatheringStatus status) => ScheduledStatuses.Contains(status);

    public static bool IsTerminal(GatheringStatus status) =>
        status is GatheringStatus.Completed or GatheringStatus.Cancelled;

    public static bool IsUpcoming(GameGathering gathering, DateTimeOffset now) =>
        gathering.StartsAtUtc > now.ToUniversalTime() && IsScheduled(gathering.Status);

    public static bool IsDue(GameGathering gathering, DateTimeOffset now) =>
        gathering.StartsAtUtc <= now.ToUniversalTime() && IsScheduled(gathering.Status);

    public static bool IsJoinOpen(GameGathering gathering, DateTimeOffset now) =>
        IsUpcoming(gathering, now) && gathering.Status != GatheringStatus.Closed;

    public static GatheringLifecycleOutcome ApplyDue(GameGathering gathering, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(gathering);
        now = now.ToUniversalTime();
        if (!IsDue(gathering, now))
            return GatheringLifecycleOutcome.None;

        var occupiedSeats = GatheringCapacity.OccupiedSeats(gathering);
        if (occupiedSeats < gathering.MinimumPlayers)
        {
            gathering.Status = GatheringStatus.Cancelled;
            gathering.CancellationReason = InsufficientParticipantsReason;
            gathering.CancelledAt = now;
            gathering.UpdatedAt = now;
            gathering.PublicationStatus = GatheringPublicationStatus.Pending;
            return GatheringLifecycleOutcome.Cancelled;
        }

        gathering.Status = GatheringStatus.Completed;
        gathering.CompletedAt = now;
        gathering.UpdatedAt = now;
        gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        return GatheringLifecycleOutcome.Completed;
    }

}
