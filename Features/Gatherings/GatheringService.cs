using System.Data;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record GatheringPromotion(long TelegramUserId, string DisplayName);
public sealed record GatheringMutationResult(GameGathering Gathering, GatheringPromotion? Promotion = null);

public sealed class GatheringService(AppDbContext dbContext, CampParticipationPolicy participationPolicy)
{
    public async Task<GatheringMutationResult> JoinAsync(
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

        var confirmed = 1 + gathering.Participants.Count(value => value.Status == GatheringParticipationStatus.Confirmed);
        var status = confirmed < gathering.MaximumPlayers
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
        if (gathering.StartsAtUtc <= now.ToUniversalTime()
            || gathering.Status is GatheringStatus.Completed or GatheringStatus.Cancelled)
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

        var promoted = GatheringRules.WithdrawParticipant(gathering, membership, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(gathering, promoted is null ? null : new GatheringPromotion(
            promoted.Participant.TelegramUserId,
            promoted.Participant.PreferredDisplayName ?? promoted.Participant.DisplayName));
    }

    private async Task<GameGathering> LockGatheringAsync(
        Guid publicId,
        string communityKey,
        CancellationToken cancellationToken) =>
        await dbContext.GameGatherings
            .FromSqlInterpolated($"SELECT * FROM \"GameGatherings\" WHERE \"PublicId\" = {publicId} AND \"CommunityKey\" = {communityKey} FOR UPDATE")
            .Include(value => value.OrganizerParticipant)
            .Include(value => value.Participants).ThenInclude(value => value.Participant)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("Сбор не найден в выбранном контексте.");

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
                cancellationToken, localDate);
        }

        return participant;
    }

    private static void EnsureJoinable(GameGathering gathering, DateTimeOffset now)
    {
        if (gathering.StartsAtUtc <= now.ToUniversalTime()
            || gathering.Status is GatheringStatus.Closed or GatheringStatus.Completed or GatheringStatus.Cancelled)
        {
            throw new InvalidOperationException("Запись в этот сбор закрыта.");
        }
    }

    private static void UpdateStatus(GameGathering gathering, DateTimeOffset now)
    {
        var confirmed = 1 + gathering.Participants.Count(value => value.Status == GatheringParticipationStatus.Confirmed);
        gathering.Status = confirmed >= gathering.MaximumPlayers
            ? GatheringStatus.Full
            : confirmed >= gathering.MinimumPlayers
                ? GatheringStatus.Ready
                : GatheringStatus.Recruiting;
        gathering.UpdatedAt = now.ToUniversalTime();
    }
}
