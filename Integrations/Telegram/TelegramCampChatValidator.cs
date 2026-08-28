using oyinQ.Bot.Features.Communities;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramCampChatValidator(ITelegramBotClient botClient) : ICampChatValidator
{
    public async Task<CampChatValidation> ValidateAsync(
        long telegramChatId,
        CancellationToken cancellationToken)
    {
        if (telegramChatId >= 0)
        {
            return new CampChatValidation(false, null, "Выберите группу или супергруппу Telegram.");
        }

        try
        {
            var chat = await botClient.GetChat(telegramChatId, cancellationToken);
            if (chat.Type is not ChatType.Group and not ChatType.Supergroup)
            {
                return new CampChatValidation(false, chat.Title, "Выбранный чат не является группой Telegram.");
            }

            var bot = await botClient.GetMe(cancellationToken);
            var membership = await botClient.GetChatMember(telegramChatId, bot.Id, cancellationToken);
            if (membership.Status is ChatMemberStatus.Left or ChatMemberStatus.Kicked)
            {
                return new CampChatValidation(false, chat.Title, "Добавьте бота в выбранную группу и повторите попытку.");
            }

            return new CampChatValidation(true, chat.Title, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new CampChatValidation(
                false,
                null,
                $"Бот не может открыть выбранную группу: {exception.Message}");
        }
    }
}
