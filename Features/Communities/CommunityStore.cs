using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;

namespace oyinQ.Bot.Features.Communities;

public interface ICommunityStore
{
    Task<BotCommunity?> FindByKeyAsync(string communityKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<BotCommunity>> ListActiveAsync(CancellationToken cancellationToken);
}

public sealed class CommunityStore(AppDbContext dbContext) : ICommunityStore
{
    public async Task<BotCommunity?> FindByKeyAsync(
        string communityKey,
        CancellationToken cancellationToken) =>
        (await dbContext.OyinQCommunities.AsNoTracking().SingleOrDefaultAsync(
            value => value.IsActive && value.DeletedAt == null && value.Key == communityKey,
            cancellationToken))?.ToBotCommunity();

    public async Task<IReadOnlyList<BotCommunity>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var entities = await dbContext.OyinQCommunities.AsNoTracking()
            .Where(value => value.IsActive && value.DeletedAt == null)
            .OrderBy(value => value.Name)
            .ToArrayAsync(cancellationToken);
        return entities.Select(value => value.ToBotCommunity()).ToArray();
    }
}
