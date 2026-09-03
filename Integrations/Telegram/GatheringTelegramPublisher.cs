using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class GatheringTelegramPublisher(
    ITelegramGroupMessageSender groupMessageSender,
    ITelegramBotClient botClient,
    GatheringPresentationService presentationService,
    ILogger<GatheringTelegramPublisher> logger)
{
    public async Task<Message> PublishAsync(
        GameGathering gathering,
        BotCommunity community,
        CancellationToken cancellationToken)
    {
        var announcement = presentationService.BuildTelegramAnnouncement(gathering, community);
        var keyboard = await BuildKeyboardAsync(gathering, community, announcement.BggUrl, cancellationToken);

        if (announcement.ImageUrl is not null)
        {
            try
            {
                return await groupMessageSender.SendPhotoAsync(
                    community.Key,
                    InputFile.FromUri(announcement.ImageUrl),
                    announcement.HtmlText,
                    ParseMode.Html,
                    keyboard,
                    cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    exception,
                    "Could not publish gathering {GatheringPublicId} as a photo; falling back to text.",
                    gathering.PublicId);
            }
        }

        return await groupMessageSender.SendMessageAsync(
            community.Key,
            announcement.HtmlText,
            ParseMode.Html,
            keyboard,
            cancellationToken);
    }

    public async Task UpdateAsync(
        GameGathering gathering,
        BotCommunity community,
        CancellationToken cancellationToken)
    {
        if (gathering.TelegramChatId is not { } chatId
            || gathering.TelegramMessageId is not { } messageId)
        {
            return;
        }

        var announcement = presentationService.BuildTelegramAnnouncement(gathering, community);
        var keyboard = await BuildKeyboardAsync(gathering, community, announcement.BggUrl, cancellationToken);
        try
        {
            await botClient.EditMessageCaption(
                chatId,
                messageId,
                caption: announcement.HtmlText,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                exception,
                "Gathering {GatheringPublicId} announcement is not a photo; updating it as text.",
                gathering.PublicId);
            await botClient.EditMessageText(
                chatId,
                messageId,
                announcement.HtmlText,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
    }

    private async Task<InlineKeyboardMarkup> BuildKeyboardAsync(
        GameGathering gathering,
        BotCommunity community,
        string? bggUrl,
        CancellationToken cancellationToken)
    {
        var bot = await botClient.GetMe(cancellationToken);
        if (string.IsNullOrWhiteSpace(bot.Username))
        {
            throw new InvalidOperationException("Telegram bot username is required for gathering deep links.");
        }

        var parameter = MiniAppStartParameter.ForGathering(community.Key, gathering.PublicId);
        var url = TelegramBotDeepLinks.BuildStart(bot.Username, parameter);
        var row = new List<InlineKeyboardButton> { InlineKeyboardButton.WithUrl("Открыть сбор", url) };
        if (bggUrl is not null) row.Add(InlineKeyboardButton.WithUrl("BGG", bggUrl));
        var rows = new List<IEnumerable<InlineKeyboardButton>> { row };
        var bggId = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson).BggId;
        if (bggId is > 0)
        {
            var collectionParameter = MiniAppStartParameter.ForCollectionGame(community.Key, bggId.Value);
            rows.Add([InlineKeyboardButton.WithUrl("В коллекции",
                TelegramBotDeepLinks.BuildStart(bot.Username, collectionParameter))]);
        }
        return new InlineKeyboardMarkup(rows);
    }
}
