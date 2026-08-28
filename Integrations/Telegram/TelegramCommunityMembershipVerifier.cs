using oyinQ.Bot.Features.Communities;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramCommunityMembershipVerifier(ITelegramBotClient botClient)
    : ICommunityMembershipVerifier
{
    public async Task<bool> IsMemberAsync(
        long telegramChatId,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var member = await botClient.GetChatMember(
            telegramChatId,
            telegramUserId,
            cancellationToken);

        return member.Status is ChatMemberStatus.Creator
            or ChatMemberStatus.Administrator
            or ChatMemberStatus.Member
            or ChatMemberStatus.Restricted;
    }
}
