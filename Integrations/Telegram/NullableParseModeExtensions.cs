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
        botClient.SendMessage(
            chatId,
            text,
            parseMode: parseMode.GetValueOrDefault(),
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);

    public static Task<Message> EditMessageText(
        this ITelegramBotClient botClient,
        ChatId chatId,
        int messageId,
        string text,
        ParseMode? parseMode,
        InlineKeyboardMarkup replyMarkup,
        CancellationToken cancellationToken = default) =>
        botClient.EditMessageText(
            chatId,
            messageId,
            text,
            parseMode: parseMode.GetValueOrDefault(),
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
}
