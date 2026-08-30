using Telegram.Bot;

namespace oyinQ.Bot.Integrations.Telegram;

public interface ITelegramMessageDeletionClient
{
    Task DeleteAsync(long chatId, int messageId, CancellationToken cancellationToken);
}

public sealed class TelegramMessageDeletionClient(ITelegramBotClient botClient)
    : ITelegramMessageDeletionClient
{
    public Task DeleteAsync(long chatId, int messageId, CancellationToken cancellationToken) =>
        botClient.DeleteMessage(chatId, messageId, cancellationToken);
}
