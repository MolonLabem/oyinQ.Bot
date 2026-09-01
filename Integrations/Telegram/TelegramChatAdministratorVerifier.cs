using oyinQ.Bot.Features.Admin;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramChatAdministratorVerifier(
    ITelegramBotClient botClient,
    ILogger<TelegramChatAdministratorVerifier> logger) : ITelegramChatAdministratorVerifier
{
    public async Task<bool> IsAdministratorAsync(long telegramChatId, long telegramUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var member = await botClient.GetChatMember(telegramChatId, telegramUserId, cancellationToken);
            return member.Status is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception,
                "Could not verify Telegram administrator {TelegramUserId} in chat {TelegramChatId}.",
                telegramUserId, telegramChatId);
            return false;
        }
    }
}
