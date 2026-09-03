using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public enum GatheringLifecycleOutcome
{
    None,
    Completed,
    Delete
}

public static class GatheringLifecycle
{
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
        if (occupiedSeats < gathering.MinimumPlayers) return GatheringLifecycleOutcome.Delete;

        gathering.Status = GatheringStatus.Completed;
        gathering.CompletedAt = now;
        gathering.UpdatedAt = now;
        gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        return GatheringLifecycleOutcome.Completed;
    }

    public static TelegramMessageCleanup? CreateCleanup(GameGathering gathering, DateTimeOffset now) =>
        gathering.TelegramChatId is { } chatId && gathering.TelegramMessageId is { } messageId
            ? new TelegramMessageCleanup
            {
                TelegramChatId = chatId,
                TelegramMessageId = messageId,
                CreatedAt = now.ToUniversalTime()
            }
            : null;
}
