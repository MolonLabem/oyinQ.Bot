using Telegram.Bot;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed record TelegramCommunityOnboardingResult(bool Sent, string? Warning);

public interface ITelegramCommunityOnboardingService
{
    Task<TelegramCommunityOnboardingResult> SendAsync(long telegramChatId,
        CancellationToken cancellationToken);
}

public sealed class TelegramCommunityOnboardingService(
    ITelegramBotClient botClient,
    ILogger<TelegramCommunityOnboardingService> logger) : ITelegramCommunityOnboardingService
{
    public async Task<TelegramCommunityOnboardingResult> SendAsync(long telegramChatId,
        CancellationToken cancellationToken)
    {
        var result = await TelegramCommunityOnboardingDelivery.TrySendAsync(
            async token => await botClient.SendMessage(telegramChatId,
                TelegramEntryText.CommunityOnboarding, cancellationToken: token), cancellationToken,
            exception => logger.LogWarning(exception,
                "Community was created, but Telegram onboarding could not be sent to chat {TelegramChatId}.",
                telegramChatId));
        if (result.Sent)
        {
            logger.LogInformation("Telegram onboarding sent to newly managed chat {TelegramChatId}.",
                telegramChatId);
            return result;
        }
        return result;
    }
}

public static class TelegramCommunityOnboardingDelivery
{
    public static async Task<TelegramCommunityOnboardingResult> TrySendAsync(
        Func<CancellationToken, Task> send, CancellationToken cancellationToken,
        Action<Exception>? onFailure = null)
    {
        try
        {
            await send(cancellationToken);
            return new(true, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            onFailure?.Invoke(exception);
            return new(false,
                "Сообщество создано, но Telegram не принял приветственное сообщение. Проверьте права бота в группе и отправьте участникам команду /oiynq.");
        }
    }
}
