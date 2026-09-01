using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Admin;

public sealed class AdminHandler(
    ITelegramBotClient botClient,
    IAdminAuthorizationService authorization,
    MiniAppLinkBuilder links)
{
    public async Task HandleCommandAsync(Message message, long telegramUserId,
        CancellationToken cancellationToken)
    {
        if (!await authorization.CanOpenAdminPanelAsync(telegramUserId, cancellationToken))
        {
            await botClient.SendMessage(message.Chat.Id,
                "Админ-панель доступна администраторам зарегистрированных чатов OyinQ.",
                cancellationToken: cancellationToken);
            return;
        }

        var url = links.Admin();
        await botClient.SendMessage(telegramUserId,
            "Администрирование OyinQ доступно в Mini App.",
            replyMarkup: new InlineKeyboardMarkup([[
                InlineKeyboardButton.WithWebApp("Открыть администрирование", new WebAppInfo { Url = url })
            ]]), cancellationToken: cancellationToken);
    }
}
