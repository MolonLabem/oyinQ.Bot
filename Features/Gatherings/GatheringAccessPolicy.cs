using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public static class GatheringAccessPolicy
{
    public static bool RequiresRegistration(BotMode mode) => mode == BotMode.Camp;

    public static bool CanJoin(GameGathering gathering, bool isOrganizer, bool hasActiveParticipation,
        DateTimeOffset now) => !isOrganizer && !hasActiveParticipation
        && GatheringLifecycle.IsJoinOpen(gathering, now);

    public static bool CanLeave(GameGathering gathering, bool isOrganizer, bool hasActiveParticipation,
        DateTimeOffset now) => !isOrganizer && hasActiveParticipation
        && GatheringLifecycle.IsUpcoming(gathering, now);

    public static bool CanManage(GameGathering gathering, bool isOrganizer, DateTimeOffset now) =>
        isOrganizer && GatheringLifecycle.IsUpcoming(gathering, now);

    public static bool CanClose(GameGathering gathering, bool isOrganizer, DateTimeOffset now) =>
        CanManage(gathering, isOrganizer, now) && gathering.Status != GatheringStatus.Closed;

    public static bool CanReopen(GameGathering gathering, bool isOrganizer, DateTimeOffset now) =>
        CanManage(gathering, isOrganizer, now) && gathering.Status == GatheringStatus.Closed;

    public static bool CanCancel(GameGathering gathering, bool isOrganizer, DateTimeOffset now) =>
        CanManage(gathering, isOrganizer, now);

    public static void RequireOrganizer(GameGathering gathering, long telegramUserId)
    {
        if (gathering.OrganizerParticipant.TelegramUserId != telegramUserId)
            throw new UnauthorizedAccessException("Управлять сбором может только организатор.");
    }

}
