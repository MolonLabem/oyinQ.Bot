using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Notifications;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record UnderfilledGatheringNotification(string GameName, int MinimumPlayers,
    int OccupiedSeats, IReadOnlyList<long> TelegramUserIds, Guid GatheringPublicId = default);

// Business-facing semantic intents. Only the central dispatcher delivers them to Telegram.
public sealed class GatheringNotificationService(AppDbContext dbContext, NotificationService notifications)
{
    public static UnderfilledGatheringNotification CaptureUnderfilled(GameGathering g) => new(
        GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson).Name, g.MinimumPlayers, GatheringCapacity.OccupiedSeats(g),
        g.Participants.Where(x => x.Status == GatheringParticipationStatus.Confirmed).Select(x => x.Participant.TelegramUserId)
            .Prepend(g.OrganizerParticipant.TelegramUserId).Distinct().ToArray(), g.PublicId);

    public Task NotifyUnderfilledAsync(UnderfilledGatheringNotification value, CancellationToken ct) => Many(value.TelegramUserIds,
        NotificationKind.GatheringFailed, value.GatheringPublicId.ToString("N"),
        $"Сбор «{value.GameName}» не состоялся.\n\nНужно минимум {value.MinimumPlayers} игроков, ожидалось {value.OccupiedSeats}.", null, value.GatheringPublicId, ct);

    public async Task NotifyTimeChangedAsync(Guid id, CancellationToken ct)
    {
        var g = await Load(id, ct); if (g is null) return;
        await Many(Recipients(g), NotificationKind.GatheringTimeChanged, $"{id:N}:{g.UpdatedAt.UtcTicks}",
            $"Изменилось время сбора «{Name(g)}»: {GatheringPresentationService.FormatLocalDateTime(g.StartsAtUtc, g.Community.TimeZoneId)}.", g.CommunityKey, id, ct);
    }
    public async Task NotifyDetailsChangedAsync(Guid id, CancellationToken ct)
    {
        var g = await Load(id, ct); if (g is null) return;
        await Many(Recipients(g), NotificationKind.GatheringDetailsChanged, $"{id:N}:{g.UpdatedAt.UtcTicks}",
            $"Обновлены детали сбора «{Name(g)}». Проверьте описание и условия участия.", g.CommunityKey, id, ct);
    }
    public async Task NotifyCancellationAsync(Guid id, CancellationToken ct)
    {
        var g = await Load(id, ct); if (g is null) return;
        await Many(Recipients(g), NotificationKind.GatheringCancelled, id.ToString("N"),
            $"Сбор «{Name(g)}» отменён." + (string.IsNullOrWhiteSpace(g.CancellationReason) ? "" : $"\n\nПричина: {g.CancellationReason}"), g.CommunityKey, id, ct);
    }
    public async Task NotifyFullAsync(Guid id, CancellationToken ct)
    {
        var g = await Load(id, ct);
        if (g is null || !GatheringLifecycle.ScheduledStatuses.Contains(g.Status) || GatheringCapacity.OccupiedSeats(g) < g.MaximumPlayers) return;
        // One full notice per recipient and gathering, even if seats open and refill repeatedly.
        await Many(g.Participants.Where(x => x.Status == GatheringParticipationStatus.Confirmed).Select(x => x.Participant.TelegramUserId)
            .Append(g.OrganizerParticipant.TelegramUserId), NotificationKind.GatheringFull, id.ToString("N"),
            $"Сбор «{Name(g)}» полностью набран.", g.CommunityKey, id, ct);
    }
    public async Task NotifyPromotionsAsync(string key, Guid id, IEnumerable<GatheringPromotion> promotions, CancellationToken ct)
    {
        foreach (var promotion in promotions)
        {
            var joinedAt = await dbContext.GameGatheringParticipants.Where(x => x.GameGathering.PublicId == id
                && x.Participant.TelegramUserId == promotion.TelegramUserId).Select(x => (DateTimeOffset?)x.JoinedAt).SingleOrDefaultAsync(ct);
            await notifications.EnqueueAsync(new(promotion.TelegramUserId, NotificationKind.WaitlistPromotion, $"{id:N}:{joinedAt?.UtcTicks}",
                $"{promotion.DisplayName}, для вас освободилось место в сборе.", key, id), ct);
        }
    }
    public async Task NotifyWithdrawalAsync(GatheringWithdrawalOutcome outcome, CancellationToken ct)
    {
        if (outcome.PreviousStatus != GatheringParticipationStatus.Confirmed) return;
        var g = await Load(outcome.GatheringPublicId, ct);
        var eventKey = $"{outcome.GatheringPublicId:N}:{outcome.DepartingParticipant.TelegramUserId}:{g?.UpdatedAt.UtcTicks}";
        if (outcome.Promotion is { } promotion)
        {
            await NotifyPromotionsAsync(outcome.CommunityKey, outcome.GatheringPublicId, [promotion], ct);
            await notifications.EnqueueAsync(new(outcome.OrganizerTelegramUserId, NotificationKind.OrganizerReplacement, eventKey,
                $"ℹ️ {outcome.DepartingParticipant.DisplayName} вышел из сбора «{outcome.GameName}».\n\nЕго место занял {promotion.DisplayName} из листа ожидания.",
                outcome.CommunityKey, outcome.GatheringPublicId), ct);
        }
        else
        {
            var kind = outcome.MissingPlayers > 0 ? NotificationKind.OrganizerBelowMinimum : NotificationKind.OrganizerParticipantLeft;
            var text = $"{outcome.DepartingParticipant.DisplayName} вышел из сбора «{outcome.GameName}».";
            if (outcome.MissingPlayers > 0) text = "⚠️ " + text + $"\n\nСейчас участников: {outcome.OccupiedSeats}. Минимум для игры: {outcome.MinimumPlayers}. Нужно найти ещё {outcome.MissingPlayers}.";
            await notifications.EnqueueAsync(new(outcome.OrganizerTelegramUserId, kind, eventKey, text, outcome.CommunityKey, outcome.GatheringPublicId), ct);
        }
    }
    private Task<GameGathering?> Load(Guid id, CancellationToken ct) => dbContext.GameGatherings.AsNoTracking()
        .Include(x => x.Community).Include(x => x.OrganizerParticipant).Include(x => x.Guests)
        .Include(x => x.Participants).ThenInclude(x => x.Participant).SingleOrDefaultAsync(x => x.PublicId == id, ct);
    private static string Name(GameGathering g)
    {
        try { return GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson).Name; }
        catch (InvalidOperationException) { return "Игра"; }
    }
    private static IEnumerable<long> Recipients(GameGathering g) => g.Participants.Where(x => x.Status is GatheringParticipationStatus.Confirmed or GatheringParticipationStatus.Waitlisted)
        .Select(x => x.Participant.TelegramUserId);
    private async Task Many(IEnumerable<long> recipients, NotificationKind kind, string eventKey, string text, string? key, Guid id, CancellationToken ct)
    { foreach (var recipient in recipients.Distinct()) await notifications.EnqueueAsync(new(recipient, kind, eventKey, text, key, id), ct); }
}
