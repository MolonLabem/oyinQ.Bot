using System.Data;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringService(AppDbContext dbContext)
{
    public async Task<GameGathering> JoinAsync(
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
        EnsureJoinable(gathering);
        var participant = await RequireContextParticipantAsync(gathering, telegramUserId, cancellationToken);
        if (gathering.OrganizerParticipantId == participant.Id)
        {
            await transaction.CommitAsync(cancellationToken);
            return gathering;
        }

        var existing = gathering.Participants.SingleOrDefault(value => value.ParticipantId == participant.Id);
        if (existing is not null && existing.Status is GatheringParticipationStatus.Confirmed or GatheringParticipationStatus.Waitlisted)
        {
            await transaction.CommitAsync(cancellationToken);
            return gathering;
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
        return gathering;
    }

    public async Task<GameGathering> LeaveAsync(
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
        var participant = await RequireContextParticipantAsync(gathering, telegramUserId, cancellationToken);
        if (gathering.OrganizerParticipantId == participant.Id)
        {
            throw new InvalidOperationException("Организатор не может покинуть собственный сбор. Отмените сбор.");
        }

        var membership = gathering.Participants.SingleOrDefault(value => value.ParticipantId == participant.Id);
        if (membership is null || membership.Status == GatheringParticipationStatus.Withdrawn)
        {
            await transaction.CommitAsync(cancellationToken);
            return gathering;
        }

        var shouldPromote = membership.Status == GatheringParticipationStatus.Confirmed;
        membership.Status = GatheringParticipationStatus.Withdrawn;
        membership.WithdrawnAt = now.ToUniversalTime();
        membership.AttendanceOutcome = AttendanceOutcome.CancelledInAdvance;
        if (shouldPromote)
        {
            var promoted = gathering.Participants
                .Where(value => value.Status == GatheringParticipationStatus.Waitlisted)
                .OrderBy(value => value.JoinedAt)
                .ThenBy(value => value.Id)
                .FirstOrDefault();
            if (promoted is not null)
            {
                promoted.Status = GatheringParticipationStatus.Confirmed;
            }
        }

        UpdateStatus(gathering, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return gathering;
    }

    private async Task<GameGathering> LockGatheringAsync(
        Guid publicId,
        string communityKey,
        CancellationToken cancellationToken) =>
        await dbContext.GameGatherings
            .FromSqlInterpolated($"SELECT * FROM \"GameGatherings\" WHERE \"PublicId\" = {publicId} AND \"CommunityKey\" = {communityKey} FOR UPDATE")
            .Include(value => value.OrganizerParticipant)
            .Include(value => value.Participants)
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
        var mode = await dbContext.OyinQCommunities.AsNoTracking()
            .Where(value => value.Key == gathering.CommunityKey)
            .Select(value => value.Mode)
            .SingleAsync(cancellationToken);
        if (GatheringAccessPolicy.RequiresRegistration(mode)
            && !await dbContext.CampRegistrations.AnyAsync(
                value => value.Camp.BotChatKey == gathering.CommunityKey && value.ParticipantId == participant.Id,
                cancellationToken))
        {
            throw new UnauthorizedAccessException("Сначала завершите регистрацию в кэмпе.");
        }

        return participant;
    }

    private static void EnsureJoinable(GameGathering gathering)
    {
        if (gathering.Status is GatheringStatus.Closed or GatheringStatus.Completed or GatheringStatus.Cancelled)
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
