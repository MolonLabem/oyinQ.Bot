using Telegram.Bot;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramBotProfileSetupService(
    ITelegramBotClient botClient,
    MiniAppLinkBuilder links,
    ILogger<TelegramBotProfileSetupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var miniAppUrl = links.App();
        var configured = await TelegramStartupRetry.RunAsync(
            async attemptToken =>
            {
                await botClient.SetChatMenuButton(
                    menuButton: new MenuButtonWebApp
                    {
                        Text = "Открыть OyinQ",
                        WebApp = new WebAppInfo { Url = miniAppUrl }
                    },
                    cancellationToken: attemptToken);
                await botClient.SetMyShortDescription(TelegramBotProfile.ShortDescription,
                    cancellationToken: attemptToken);
                await botClient.SetMyDescription(TelegramBotProfile.Description,
                    cancellationToken: attemptToken);
                await botClient.SetMyCommands(TelegramBotProfile.PrivateCommands,
                    scope: BotCommandScope.AllPrivateChats(), cancellationToken: attemptToken);
                await botClient.SetMyCommands(TelegramBotProfile.GroupCommands,
                    scope: BotCommandScope.AllGroupChats(), cancellationToken: attemptToken);
            },
            "bot profile configuration", logger, cancellationToken);

        if (configured) logger.LogInformation(
            "Telegram bot profile configured: {PrivateCommandCount} private commands, {GroupCommandCount} group commands, Mini App menu {MiniAppUrl}.",
            TelegramBotProfile.PrivateCommands.Count, TelegramBotProfile.GroupCommands.Count, miniAppUrl);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
