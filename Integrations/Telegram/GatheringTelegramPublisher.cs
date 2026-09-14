using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
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
    public async Task<Message> PublishAsync(GameGathering gathering, BotCommunity community, CancellationToken ct) =>
        await (await PrepareNewAsync(gathering, community, ct))(ct);

    public async Task<Func<CancellationToken, Task<Message>>> PrepareNewAsync(GameGathering gathering, BotCommunity community, CancellationToken ct)
    {
        var announcement = presentationService.BuildTelegramAnnouncement(gathering, community);
        var keyboard = await BuildKeyboardAsync(gathering, community, ct);
        var text = await groupMessageSender.PrepareMessageAsync(community.Key, announcement.HtmlText, ParseMode.Html, keyboard, ct);
        if (announcement.ImageUrl is null) return text;
        var photo = await groupMessageSender.PreparePhotoAsync(community.Key, InputFile.FromUri(announcement.ImageUrl), announcement.HtmlText, ParseMode.Html, keyboard, ct);
        return async token =>
        {
            try { return await photo(token); }
            catch (ApiRequestException exception) when (exception.ErrorCode == 400 && IsPhotoRejected(exception))
            {
                logger.LogWarning(exception, "Gathering photo rejected; using text for {GatheringPublicId}.", gathering.PublicId);
                return await text(token);
            }
        };
    }

    private static bool IsPhotoRejected(ApiRequestException e) =>
        e.Message.Contains("failed to get HTTP URL content", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("wrong file identifier", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("IMAGE_PROCESS_FAILED", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("PHOTO_INVALID_DIMENSIONS", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("wrong type of the web page content", StringComparison.OrdinalIgnoreCase);
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

        await (await PrepareUpdateAsync(gathering, community, cancellationToken))(cancellationToken);
    }

    public async Task<Func<CancellationToken, Task>> PrepareUpdateAsync(GameGathering gathering, BotCommunity community, CancellationToken ct)
    {
        var announcement = presentationService.BuildTelegramAnnouncement(gathering, community);
        var keyboard = await BuildKeyboardAsync(gathering, community, ct);
        return token => UpdatePreparedAsync(gathering, announcement.HtmlText, keyboard, token);
    }

    private async Task UpdatePreparedAsync(GameGathering gathering, string htmlText, InlineKeyboardMarkup keyboard, CancellationToken cancellationToken)
    {
        var chatId = gathering.TelegramChatId!.Value;
        var messageId = gathering.TelegramMessageId!.Value;

        try
        {
            await botClient.EditMessageCaption(
                chatId,
                messageId,
                caption: htmlText,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException exception) when (IsMessageNotModified(exception))
        {
            return;
        }
        catch (ApiRequestException exception) when (IsCaptionUnavailable(exception))
        {
            logger.LogDebug(
                exception,
                "Gathering {GatheringPublicId} announcement is not a photo; updating it as text.",
                gathering.PublicId);
            try
            {
                await botClient.EditMessageText(
                    chatId,
                    messageId,
                    htmlText,
                    parseMode: ParseMode.Html,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
            catch (ApiRequestException textException) when (IsMessageNotModified(textException))
            {
                return;
            }
        }
    }

    private static bool IsMessageNotModified(ApiRequestException exception) =>
        exception.ErrorCode == 400
        && exception.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase);

    private static bool IsCaptionUnavailable(ApiRequestException exception) =>
        exception.ErrorCode == 400
        && (exception.Message.Contains("there is no caption", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not a media message", StringComparison.OrdinalIgnoreCase));

    private async Task<InlineKeyboardMarkup> BuildKeyboardAsync(
        GameGathering gathering,
        BotCommunity community,
        CancellationToken cancellationToken)
    {
        var bot = await botClient.GetMe(cancellationToken);
        if (string.IsNullOrWhiteSpace(bot.Username))
        {
            throw new InvalidOperationException("Telegram bot username is required for gathering deep links.");
        }

        var parameter = MiniAppStartParameter.ForGathering(community.Key, gathering.PublicId);
        var url = TelegramBotDeepLinks.BuildMainMiniApp(bot.Username, parameter);
        return new InlineKeyboardMarkup([[
            InlineKeyboardButton.WithUrl("Открыть сбор", url)
        ]]);
    }
}
