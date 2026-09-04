using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record GatheringParticipantNotification(long TelegramUserId, string DisplayName)
{
    public static GatheringParticipantNotification Capture(Participant participant) =>
        new(participant.TelegramUserId, ParticipantPresentation.GetDisplayName(participant));
}

public sealed record GatheringPromotion(long TelegramUserId, string DisplayName)
{
    public static GatheringPromotion Capture(Participant participant) =>
        new(participant.TelegramUserId, ParticipantPresentation.GetDisplayName(participant));
}

public sealed record GatheringWithdrawalOutcome(
    Guid GatheringPublicId,
    string CommunityKey,
    string GameName,
    long OrganizerTelegramUserId,
    GatheringParticipationStatus PreviousStatus,
    GatheringParticipantNotification DepartingParticipant,
    GatheringPromotion? Promotion,
    int OccupiedSeats,
    int MinimumPlayers)
{
    public int MissingPlayers => Math.Max(0, MinimumPlayers - OccupiedSeats);

    public static GatheringWithdrawalOutcome Capture(GameGathering gathering,
        GatheringParticipantWithdrawal withdrawal)
    {
        var departing = withdrawal.DepartingParticipant.Participant;
        var promoted = withdrawal.PromotedParticipant?.Participant;
        return new(
            gathering.PublicId,
            gathering.CommunityKey,
            GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson).Name,
            gathering.OrganizerParticipant.TelegramUserId,
            withdrawal.PreviousStatus,
            GatheringParticipantNotification.Capture(departing),
            promoted is null ? null : GatheringPromotion.Capture(promoted),
            withdrawal.OccupiedSeats,
            gathering.MinimumPlayers);
    }
}
