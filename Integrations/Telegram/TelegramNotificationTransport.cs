using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Notifications;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramNotificationTransport(ITelegramBotClient bot, MiniAppLinkBuilder links) : INotificationTransport
{
    public async Task<NotificationReceipt> SendAsync(Notification notification, Participant recipient, CancellationToken ct)
    {
        InlineKeyboardMarkup? keyboard = null;
        if (notification.GatheringPublicId is { } id && notification.CommunityKey is { } key && notification.Kind != NotificationKind.GatheringFailed)
            keyboard = new([[InlineKeyboardButton.WithWebApp("Открыть сбор", new WebAppInfo { Url = links.Gathering(key, id) })]]);
        if (notification.ImportPublicId is { } importId)
            keyboard = new([[InlineKeyboardButton.WithWebApp("Открыть коллекцию", new WebAppInfo { Url = notification.CommunityKey is { } campKey ? links.CampImport(campKey, importId) : links.ProfileCollection(importId) })]]);
        if (notification.Kind == NotificationKind.PostingTopicUnavailable)
            keyboard = new([[InlineKeyboardButton.WithWebApp("Открыть настройки", new WebAppInfo { Url = links.Admin() })]]);
        try
        {
            var message = await bot.SendMessage(recipient.TelegramUserId, notification.Text, replyMarkup: keyboard, cancellationToken: ct);
            return new(message.Id);
        }
        catch (ApiRequestException e) when (e.ErrorCode == 403 || (e.ErrorCode == 400 && e.Message.Contains("chat not found", StringComparison.OrdinalIgnoreCase)))
        { return new(null, "cannot_message_user", CannotMessage: true); }
        catch (ApiRequestException e)
        { return new(null, $"telegram_{e.ErrorCode}", Retryable: e.ErrorCode == 429 || e.ErrorCode >= 500); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        { return new(null, "delivery_outcome_unknown", Uncertain: true); }
    }
}
