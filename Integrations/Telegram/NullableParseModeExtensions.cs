using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot;

internal static class NullableParseModeExtensions
{
    public static Task<Message> SendMessage(
        this ITelegramBotClient botClient,
        ChatId chatId,
        string text,
        ParseMode? parseMode,
        InlineKeyboardMarkup replyMarkup,
        CancellationToken cancellationToken = default) =>
        botClient.SendRequest(
            new SendMessageRequest
            {
                ChatId = chatId,
                Text = text,
                ParseMode = parseMode.GetValueOrDefault(),
                ReplyMarkup = replyMarkup
            },
            cancellationToken);

    public static Task<Message> EditMessageText(
        this ITelegramBotClient botClient,
        ChatId chatId,
        int messageId,
        string text,
        ParseMode? parseMode,
        InlineKeyboardMarkup replyMarkup,
        CancellationToken cancellationToken = default) =>
        botClient.SendRequest(
            new EditMessageTextRequest
            {
                ChatId = chatId,
                MessageId = messageId,
                Text = text,
                ParseMode = parseMode.GetValueOrDefault(),
                ReplyMarkup = replyMarkup
            },
            cancellationToken);
}
