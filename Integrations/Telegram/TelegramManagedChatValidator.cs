using oyinQ.Bot.Features.Communities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramManagedChatValidator(ITelegramBotClient botClient) : IManagedChatValidator
{
    public async Task<ManagedChatValidation> ValidateAsync(long telegramChatId,
        long requestingAdministratorId, CancellationToken cancellationToken)
    {
        if (telegramChatId >= 0)
            return new(false, null, null, "Выберите группу или супергруппу Telegram.");
        try
        {
            var chat = await botClient.GetChat(telegramChatId, cancellationToken);
            if (chat.Type is not ChatType.Group and not ChatType.Supergroup)
                return new(false, chat.Title, chat.Username, "Выбранный чат не является группой Telegram.");

            var requester = await botClient.GetChatMember(telegramChatId, requestingAdministratorId, cancellationToken);
            if (requester.Status is not ChatMemberStatus.Creator and not ChatMemberStatus.Administrator)
                return new(false, chat.Title, chat.Username, "Создатель сообщества должен быть администратором выбранной группы.");

            var bot = await botClient.GetMe(cancellationToken);
            var membership = await botClient.GetChatMember(telegramChatId, bot.Id, cancellationToken);
            if (membership.Status is ChatMemberStatus.Left or ChatMemberStatus.Kicked)
                return new(false, chat.Title, chat.Username, "Добавьте бота в выбранную группу.");
            if (membership is ChatMemberRestricted { CanSendMessages: false })
                return new(false, chat.Title, chat.Username, "Бот не может отправлять сообщения в выбранную группу.");
            if (membership.Status == ChatMemberStatus.Member && chat.Permissions?.CanSendMessages == false)
                return new(false, chat.Title, chat.Username, "Участникам группы запрещено отправлять сообщения; назначьте бота администратором.");
            return new(true, chat.Title, chat.Username, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, null, null, $"Бот не может проверить выбранную группу: {exception.Message}");
        }
    }
}
