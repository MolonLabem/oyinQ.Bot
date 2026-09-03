using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

internal static class GatheringWriteStore
{
    public static async Task<GameGathering> LockAsync(AppDbContext dbContext, Guid publicId,
        string communityKey, CancellationToken cancellationToken)
    {
        var query = string.Equals(dbContext.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal)
            ? dbContext.GameGatherings.FromSqlInterpolated(
                $"SELECT * FROM \"GameGatherings\" WHERE \"PublicId\" = {publicId} AND \"CommunityKey\" = {communityKey} FOR UPDATE")
            : dbContext.GameGatherings.Where(x => x.PublicId == publicId && x.CommunityKey == communityKey);

        return await query
            .Include(x => x.OrganizerParticipant)
            .Include(x => x.Participants).ThenInclude(x => x.Participant)
            .Include(x => x.Guests)
            .Include(x => x.Expansions)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Сбор не найден в этом сообществе.");
    }
}
