using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public static class GatheringAccessPolicy
{
    public static bool RequiresRegistration(BotMode mode) => mode == BotMode.Camp;

    public static bool CanManage(
        GameGathering gathering,
        string communityKey,
        long telegramUserId) =>
        string.Equals(gathering.CommunityKey, communityKey, StringComparison.Ordinal)
        && gathering.OrganizerParticipant.TelegramUserId == telegramUserId;
}
