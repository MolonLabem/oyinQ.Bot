using Microsoft.EntityFrameworkCore;
using Npgsql;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class ParticipantIdentityService(AppDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Participant> GetOrCreateAsync(long telegramUserId, string? username,
        string? displayName, string? communityKey, CancellationToken cancellationToken,
        bool privateMessageReceived = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(telegramUserId);
        var now = timeProvider.GetUtcNow();
        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            x => x.TelegramUserId == telegramUserId, cancellationToken);
        if (participant is null)
        {
            participant = ParticipantIdentityPolicy.Create(telegramUserId, username, displayName, now);
            dbContext.Participants.Add(participant);
            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Participants_TelegramUserId" })
            {
                dbContext.Entry(participant).State = EntityState.Detached;
                participant = await dbContext.Participants.SingleAsync(
                    x => x.TelegramUserId == telegramUserId, cancellationToken);
            }
        }
        ParticipantIdentityPolicy.RefreshTrustedPresentation(participant, username, displayName, now);
        if (communityKey is not null) participant.ActiveCommunityKey = communityKey;
        if (privateMessageReceived) { participant.PrivateChatStartedAt = now; participant.TelegramDeliveryBlockedAt = null; }
        participant.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return participant;
    }
}
