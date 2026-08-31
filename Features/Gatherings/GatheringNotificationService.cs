using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record UnderfilledGatheringNotification(string GameName, int MinimumPlayers,
    int ConfirmedPlayers, IReadOnlyList<long> TelegramUserIds);

public sealed class GatheringNotificationService(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    MiniAppLinkBuilder links,
    ILogger<GatheringNotificationService> logger)
{
    public static UnderfilledGatheringNotification CaptureUnderfilled(GameGathering gathering)
    {
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        var recipients = new[] { gathering.OrganizerParticipant.TelegramUserId }
            .Concat(gathering.Participants
                .Where(x => x.Status == GatheringParticipationStatus.Confirmed)
                .Select(x => x.Participant.TelegramUserId))
            .Distinct().ToArray();
        return new(snapshot.Name, gathering.MinimumPlayers,
            1 + gathering.Participants.Count(x => x.Status == GatheringParticipationStatus.Confirmed),
            recipients);
    }

    public Task NotifyUnderfilledAsync(UnderfilledGatheringNotification notification,
        CancellationToken cancellationToken) => SendManyAsync(notification.TelegramUserIds,
        $"Сбор «{notification.GameName}» не состоялся.\n\nНужно минимум {notification.MinimumPlayers} игроков, записалось {notification.ConfirmedPlayers}.",
        null, null, cancellationToken);

    public async Task NotifyTimeChangedAsync(Guid gatheringPublicId, CancellationToken cancellationToken)
    {
        var value = await LoadAsync(gatheringPublicId, cancellationToken);
        if (value is null) return;
        var local = TimeZoneInfo.ConvertTime(value.StartsAtUtc,
            TimeZoneInfo.FindSystemTimeZoneById(value.TimeZoneId));
        await SendManyAsync(value.Recipients,
            $"Изменилось время сбора «{value.GameName}»: {local:dd.MM.yyyy HH:mm}.",
            value.CommunityKey, gatheringPublicId, cancellationToken);
    }

    public async Task NotifyCancellationAsync(Guid gatheringPublicId, CancellationToken cancellationToken)
    {
        var value = await LoadAsync(gatheringPublicId, cancellationToken);
        if (value is null) return;
        var reason = string.IsNullOrWhiteSpace(value.CancellationReason)
            ? string.Empty : $"\n\nПричина: {value.CancellationReason}";
        await SendManyAsync(value.Recipients, $"Сбор «{value.GameName}» отменён.{reason}",
            value.CommunityKey, gatheringPublicId, cancellationToken);
    }

    private async Task<NotificationView?> LoadAsync(Guid publicId, CancellationToken cancellationToken)
    {
        var gathering = await dbContext.GameGatherings.AsNoTracking()
            .Include(x => x.Participants).ThenInclude(x => x.Participant)
            .Include(x => x.Community)
            .SingleOrDefaultAsync(x => x.PublicId == publicId, cancellationToken);
        if (gathering is null) return null;
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        var recipients = gathering.Participants
            .Where(x => x.Status is GatheringParticipationStatus.Confirmed
                or GatheringParticipationStatus.Waitlisted)
            .Select(x => x.Participant.TelegramUserId).Distinct().ToArray();
        return new(gathering.CommunityKey, gathering.Community.TimeZoneId, snapshot.Name,
            gathering.StartsAtUtc, gathering.CancellationReason, recipients);
    }

    private async Task SendManyAsync(IEnumerable<long> recipients, string text, string? communityKey,
        Guid? gatheringPublicId, CancellationToken cancellationToken)
    {
        InlineKeyboardMarkup? markup = null;
        if (communityKey is not null && gatheringPublicId is { } publicId)
        {
            var url = links.Gathering(communityKey, publicId);
            markup = new InlineKeyboardMarkup([[
                InlineKeyboardButton.WithWebApp("Открыть сбор", new WebAppInfo { Url = url })
            ]]);
        }
        foreach (var telegramUserId in recipients.Distinct())
            try
            {
                await botClient.SendMessage(telegramUserId, text, replyMarkup: markup,
                    cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Could not send gathering notification to {TelegramUserId}.",
                    telegramUserId);
            }
    }

    private sealed record NotificationView(string CommunityKey, string TimeZoneId, string GameName,
        DateTimeOffset StartsAtUtc, string? CancellationReason, IReadOnlyList<long> Recipients);
}
