using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramPollingService(
    ITelegramBotClient botClient,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramPollingService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Telegram long polling enabled for local development.");

        await botClient.DeleteWebhook(
            dropPendingUpdates: false,
            cancellationToken: stoppingToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery,
                UpdateType.MyChatMember, UpdateType.ChatMember],
            DropPendingUpdates = false
        };

        await botClient.ReceiveAsync(
            async (_, update, cancellationToken) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>();
                await handler.HandleAsync(update, cancellationToken);
            },
            (_, exception, _) =>
            {
                logger.LogError(exception, "Telegram polling error.");
                return Task.CompletedTask;
            },
            receiverOptions,
            stoppingToken);
    }
}
