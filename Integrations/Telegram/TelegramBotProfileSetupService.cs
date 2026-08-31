using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramBotProfileSetupService(
    ITelegramBotClient botClient,
    IOptions<BotOptions> options,
    ILogger<TelegramBotProfileSetupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var miniAppUrl = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/app/";
        await botClient.SetChatMenuButton(
            menuButton: new MenuButtonWebApp
            {
                Text = "Открыть OyinQ",
                WebApp = new WebAppInfo { Url = miniAppUrl }
            },
            cancellationToken: cancellationToken);
        await botClient.SetMyShortDescription(TelegramBotProfile.ShortDescription,
            cancellationToken: cancellationToken);
        await botClient.SetMyDescription(TelegramBotProfile.Description,
            cancellationToken: cancellationToken);
        await botClient.SetMyCommands(TelegramBotProfile.PrivateCommands,
            scope: BotCommandScope.AllPrivateChats(), cancellationToken: cancellationToken);
        await botClient.SetMyCommands(TelegramBotProfile.GroupCommands,
            scope: BotCommandScope.AllGroupChats(), cancellationToken: cancellationToken);

        logger.LogInformation(
            "Telegram bot profile configured: {PrivateCommandCount} private commands, {GroupCommandCount} group commands, Mini App menu {MiniAppUrl}.",
            TelegramBotProfile.PrivateCommands.Count, TelegramBotProfile.GroupCommands.Count, miniAppUrl);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
