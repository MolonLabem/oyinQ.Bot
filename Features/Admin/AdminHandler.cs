using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Admin;

public sealed class AdminHandler(
    ITelegramBotClient botClient,
    IAdministratorStore administratorStore,
    IOptions<BotOptions> botOptions)
{
    public async Task HandleCommandAsync(Message message, long telegramUserId,
        CancellationToken cancellationToken)
    {
        if (!await administratorStore.IsAdministratorAsync(telegramUserId, cancellationToken))
        {
            await botClient.SendMessage(message.Chat.Id, "Доступ запрещён.", cancellationToken: cancellationToken);
            return;
        }

        var url = $"{botOptions.Value.PublicBaseUrl.TrimEnd('/')}/app/?admin=1";
        await botClient.SendMessage(telegramUserId,
            "Администрирование OyinQ доступно в Mini App.",
            replyMarkup: new InlineKeyboardMarkup([[
                InlineKeyboardButton.WithWebApp("Открыть администрирование", new WebAppInfo { Url = url })
            ]]), cancellationToken: cancellationToken);
    }
}
