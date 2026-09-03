using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramWebhookSetupService(
    ITelegramBotClient botClient,
    IOptions<BotOptions> options,
    ILogger<TelegramWebhookSetupService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var botOptions = options.Value;
        var webhookUrl =
            $"{botOptions.PublicBaseUrl.TrimEnd('/')}/telegram/webhook/{Uri.EscapeDataString(botOptions.WebhookSecret)}";

        var configured = await TelegramStartupRetry.RunAsync(
            attemptToken => botClient.SetWebhook(
                webhookUrl,
                allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery,
                    UpdateType.MyChatMember, UpdateType.ChatMember],
                dropPendingUpdates: false,
                secretToken: botOptions.WebhookSecret,
                cancellationToken: attemptToken),
            "webhook configuration", logger, cancellationToken);

        if (configured)
            logger.LogInformation("Telegram webhook configured for {PublicBaseUrl}.", botOptions.PublicBaseUrl);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
