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
    public static GatheringLifecycleOutcome ApplyDue(GameGathering gathering, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(gathering);
        now = now.ToUniversalTime();
        if (gathering.StartsAtUtc > now
            || gathering.Status is not (GatheringStatus.Recruiting or GatheringStatus.Ready
                or GatheringStatus.Full or GatheringStatus.Closed))
            return GatheringLifecycleOutcome.None;

        var confirmedPlayers = 1 + gathering.Participants.Count(
            x => x.Status == GatheringParticipationStatus.Confirmed);
        if (confirmedPlayers < gathering.MinimumPlayers) return GatheringLifecycleOutcome.Delete;

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
