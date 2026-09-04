using System.Data;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record GatheringMutationResult(GameGathering Gathering, GatheringPromotion? Promotion = null,
    GatheringWithdrawalOutcome? Withdrawal = null);

public sealed class GatheringService(AppDbContext dbContext, CampParticipationPolicy participationPolicy, GatheringScheduleConflictService? conflicts = null, GatheringNotificationService? notificationService = null)
{
    private readonly GatheringNotificationService notifications = notificationService
        ?? new(dbContext, new Features.Notifications.NotificationService(dbContext, TimeProvider.System));
    public async Task<GatheringMutationResult> JoinAsync(
        Guid publicId,
        string communityKey,
        long telegramUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken, bool confirmScheduleConflict = false)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var gathering = await LockGatheringAsync(publicId, communityKey, cancellationToken);
        EnsureJoinable(gathering, now);
        var participant = await RequireContextParticipantAsync(gathering, telegramUserId, cancellationToken);
        if (gathering.OrganizerParticipantId == participant.Id)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(gathering);
        }

        var existing = gathering.Participants.SingleOrDefault(value => value.ParticipantId == participant.Id);
        if (existing is not null && existing.Status is GatheringParticipationStatus.Confirmed or GatheringParticipationStatus.Waitlisted)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(gathering);
        }

        await (conflicts ?? new GatheringScheduleConflictService(dbContext)).WarnAsync(participant.Id, gathering.StartsAtUtc, gathering.PublicId, confirmScheduleConflict, now, cancellationToken);
        var status = GatheringCapacity.HasAvailableSeat(gathering)
            ? GatheringParticipationStatus.Confirmed
            : GatheringParticipationStatus.Waitlisted;
        if (existing is null)
        {
            existing = new GameGatheringParticipant
            {
                ParticipantId = participant.Id,
                Status = status,
                AttendanceOutcome = AttendanceOutcome.Unknown,
                JoinedAt = now.ToUniversalTime()
            };
            gathering.Participants.Add(existing);
        }
        else
        {
            existing.Status = status;
            existing.JoinedAt = now.ToUniversalTime();
            existing.WithdrawnAt = null;
            existing.AttendanceOutcome = AttendanceOutcome.Unknown;
        }

        UpdateStatus(gathering, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifications.NotifyFullAsync(publicId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(gathering);
    }

    public async Task<GatheringMutationResult> LeaveAsync(
        Guid publicId,
        string communityKey,
        long telegramUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var gathering = await LockGatheringAsync(publicId, communityKey, cancellationToken);
        if (!GatheringLifecycle.IsUpcoming(gathering, now))
            throw new InvalidOperationException("Нельзя покинуть завершённый или отменённый сбор.");
        var participant = await RequireContextParticipantAsync(gathering, telegramUserId, cancellationToken);
        if (gathering.OrganizerParticipantId == participant.Id)
        {
            throw new InvalidOperationException("Организатор не может покинуть собственный сбор. Отмените сбор.");
        }

        var membership = gathering.Participants.SingleOrDefault(value => value.ParticipantId == participant.Id);
        if (membership is null || membership.Status == GatheringParticipationStatus.Withdrawn)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(gathering);
        }

        var withdrawal = GatheringRules.WithdrawParticipant(gathering, membership, now)!;
        var outcome = GatheringWithdrawalOutcome.Capture(gathering, withdrawal);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifications.NotifyWithdrawalAsync(outcome, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(gathering, Withdrawal: outcome);
    }

    public async Task<GatheringMutationResult> AddGuestAsync(Guid publicId, string communityKey,
        long telegramUserId, string? displayName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var gathering = await LockGatheringAsync(publicId, communityKey, cancellationToken);
        GatheringAccessPolicy.RequireOrganizer(gathering, telegramUserId);
        GatheringRules.EnsureGuestEditable(gathering, now);
        var normalizedDisplayName = GatheringRules.NormalizeGuestDisplayName(displayName);
        if (!GatheringCapacity.HasAvailableSeat(gathering))
            throw new InvalidOperationException("В сборе больше нет свободных мест.");
        gathering.Guests.Add(new GameGatheringGuest
        {
            DisplayName = normalizedDisplayName,
            CreatedByParticipantId = gathering.OrganizerParticipantId,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime()
        });
        UpdateStatus(gathering, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifications.NotifyFullAsync(publicId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(gathering);
    }

    public async Task<GatheringMutationResult> RenameGuestAsync(Guid publicId, long guestId,
        string communityKey, long telegramUserId, string? displayName, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var gathering = await LockGatheringAsync(publicId, communityKey, cancellationToken);
        GatheringAccessPolicy.RequireOrganizer(gathering, telegramUserId);
        GatheringRules.EnsureGuestEditable(gathering, now);
        var guest = gathering.Guests.SingleOrDefault(x => x.Id == guestId)
            ?? throw new KeyNotFoundException("Гость не найден в этом сборе.");
        guest.DisplayName = GatheringRules.NormalizeGuestDisplayName(displayName);
        guest.UpdatedAt = now.ToUniversalTime();
        gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        gathering.UpdatedAt = now.ToUniversalTime();
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifications.NotifyFullAsync(publicId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(gathering);
    }

    public async Task<GatheringMutationResult> RemoveGuestAsync(Guid publicId, long guestId,
        string communityKey, long telegramUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var gathering = await LockGatheringAsync(publicId, communityKey, cancellationToken);
        GatheringAccessPolicy.RequireOrganizer(gathering, telegramUserId);
        GatheringRules.EnsureGuestEditable(gathering, now);
        var guest = gathering.Guests.SingleOrDefault(x => x.Id == guestId)
            ?? throw new KeyNotFoundException("Гость не найден в этом сборе.");
        dbContext.GameGatheringGuests.Remove(guest);
        gathering.Guests.Remove(guest);
        var promoted = GatheringCapacity.PromoteFirstWaitlisted(gathering);
        UpdateStatus(gathering, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (promoted is not null) await notifications.NotifyPromotionsAsync(communityKey, publicId, [GatheringPromotion.Capture(promoted.Participant)], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(gathering, promoted is null ? null : GatheringPromotion.Capture(promoted.Participant));
    }

    private async Task<GameGathering> LockGatheringAsync(
        Guid publicId,
        string communityKey,
        CancellationToken cancellationToken) =>
        await GatheringWriteStore.LockAsync(dbContext, publicId, communityKey, cancellationToken);

    private async Task<Participant> RequireContextParticipantAsync(
        GameGathering gathering,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken)
            ?? throw new UnauthorizedAccessException("Участник не найден.");
        var context = await dbContext.OyinQCommunities.AsNoTracking()
            .Where(value => value.Key == gathering.CommunityKey)
            .Select(value => new { value.Mode, value.TimeZoneId,
                CampId = value.Camp == null ? (long?)null : value.Camp.Id })
            .SingleAsync(cancellationToken);
        if (GatheringAccessPolicy.RequiresRegistration(context.Mode))
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(gathering.StartsAtUtc,
                TimeZoneInfo.FindSystemTimeZoneById(context.TimeZoneId)).DateTime);
            await participationPolicy.RequireCompleteRegistrationAsync(context.CampId!.Value, participant.Id,
                cancellationToken, localDate, gathering.StartsAtUtc);
        }

        return participant;
    }

    private static void EnsureJoinable(GameGathering gathering, DateTimeOffset now)
    {
        if (!GatheringLifecycle.IsJoinOpen(gathering, now))
        {
            throw new InvalidOperationException("Запись в этот сбор закрыта.");
        }
    }

    private static void UpdateStatus(GameGathering gathering, DateTimeOffset now)
    {
        GatheringCapacity.SynchronizeScheduledStatus(gathering);
        gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        gathering.UpdatedAt = now.ToUniversalTime();
    }
}
