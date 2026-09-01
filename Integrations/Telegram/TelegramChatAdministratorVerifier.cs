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

    public async Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(
        long telegramChatId, CancellationToken cancellationToken)
    {
        try
        {
            var administrators = await botClient.GetChatAdministrators(telegramChatId,
                returnBots: false, cancellationToken);
            return administrators.Where(x => !x.User.IsBot).Select(x => new EligibleGroupAdministrator(
                    x.User.Id, BuildDisplayName(x.User), x.User.Username))
                .DistinctBy(x => x.TelegramUserId).ToArray();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not list Telegram administrators in chat {TelegramChatId}.",
                telegramChatId);
            throw new InvalidOperationException(
                "Telegram не позволил получить список администраторов группы. Проверьте права бота.", exception);
        }
    }

    private static string? BuildDisplayName(global::Telegram.Bot.Types.User user)
    {
        var value = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
