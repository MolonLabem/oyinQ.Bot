using Telegram.Bot;

namespace oyinQ.Bot.Integrations.Telegram;

public interface ITelegramChatForumCapabilityResolver
{
    Task<bool?> GetIsForumAsync(long telegramChatId, CancellationToken cancellationToken);
}

public sealed class TelegramChatForumCapabilityResolver(
    ITelegramBotClient botClient,
    ILogger<TelegramChatForumCapabilityResolver> logger) : ITelegramChatForumCapabilityResolver
{
    public async Task<bool?> GetIsForumAsync(long telegramChatId, CancellationToken cancellationToken)
    {
        try
        {
            return (await botClient.GetChat(telegramChatId, cancellationToken)).IsForum;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception,
                "Could not refresh Telegram forum capability for chat {TelegramChatId}.", telegramChatId);
            return null;
        }
    }
}
