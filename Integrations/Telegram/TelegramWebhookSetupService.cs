using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
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

        await botClient.SetWebhook(
            webhookUrl,
            allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
            dropPendingUpdates: false,
            secretToken: botOptions.WebhookSecret,
            cancellationToken: cancellationToken);

        var miniAppUrl = $"{botOptions.PublicBaseUrl.TrimEnd('/')}/app/";
        await botClient.SetChatMenuButton(
            menuButton: new MenuButtonWebApp
            {
                Text = "Открыть OyinQ",
                WebApp = new WebAppInfo { Url = miniAppUrl }
            },
            cancellationToken: cancellationToken);
        await botClient.SetMyShortDescription(
            "Настольные игры, сборы и коллекции клубов и кэмпов.",
            cancellationToken: cancellationToken);
        await botClient.SetMyDescription(
            "OyinQ помогает находить настольные игры, создавать сборы и присоединяться к ним. " +
            "Клубы ведут общую коллекцию, а участники кэмпов отмечают игры, которые привезут. " +
            "Все основные действия доступны в удобном Mini App.",
            cancellationToken: cancellationToken);
        await botClient.SetMyCommands(
            [
                new BotCommand { Command = "start", Description = "Открыть OyinQ" },
                new BotCommand { Command = "menu", Description = "Открыть главное меню" },
                new BotCommand { Command = "admin", Description = "Открыть админ-панель" }
            ],
            scope: BotCommandScope.AllPrivateChats(),
            cancellationToken: cancellationToken);

        logger.LogInformation("Telegram webhook, commands, and Mini App menu configured for {PublicBaseUrl}.",
            botOptions.PublicBaseUrl);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
