using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using Microsoft.EntityFrameworkCore;

namespace oyinQ.Bot.Features.Catalog;

public sealed record EffectiveCampExpansion(long BggId, string Name, string? OriginalName,
    IReadOnlyList<CampCatalogProvider> Providers, int? MinPlayers = null, int? MaxPlayers = null);

public sealed record EffectiveCampGame(ClubCollectionGame Game, bool IsInBaseCollection,
    IReadOnlyList<CampCatalogProvider> Providers, IReadOnlyList<EffectiveCampExpansion> Expansions);

public sealed class EffectiveCampCatalogService(
    AppDbContext dbContext,
    CampContributionSelectionService contributions)
{
    public async Task<IReadOnlyList<EffectiveCampGame>> LoadAsync(string communityKey,
        long? currentParticipantId, CancellationToken cancellationToken, DateOnly? attendanceDate = null)
    {
        var camp = await dbContext.Camps.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BotChatKey == communityKey, cancellationToken)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        var contributed = await contributions.GetEffectiveContributionsAsync(camp.Id, cancellationToken,
            currentParticipantId, attendanceDate);
        return Build(camp.ReadBaseCollection(), contributed);
    }

    public static IReadOnlyList<EffectiveCampGame> Build(ClubCollectionDocument baseCollection,
        IReadOnlyCollection<EffectiveCampCatalogItem> contributions)
    {
        var bases = new List<(ClubCollectionGame Game, bool InBase, IReadOnlyList<CampCatalogProvider> Providers)>();
        foreach (var game in baseCollection.Games)
        {
            var personal = contributions.SingleOrDefault(x => x.ItemType == CollectionItemType.BaseGame
                && x.BggId == game.BggId);
            bases.Add((personal is null ? game : game.WithMetadataFallback(personal.Snapshot), true,
                personal?.Providers ?? []));
        }
        bases.AddRange(contributions.Where(x => x.ItemType == CollectionItemType.BaseGame
                && bases.All(existing => existing.Game.BggId != x.BggId))
            .Select(x => (ToGame(x), false, x.Providers)));

        var contributedExpansions = contributions.Where(x => x.ItemType == CollectionItemType.Expansion)
            .ToArray();
        return bases.Select(value =>
        {
            var expansionMap = value.Game.Expansions
                .Select(x => new EffectiveCampExpansion(x.BggId, x.Name, x.OriginalName,
                    contributedExpansions.SingleOrDefault(c => c.BggId == x.BggId)?.Providers ?? [], x.MinPlayers, x.MaxPlayers))
                .Concat(contributedExpansions.Where(x => x.ParentBggIds.Contains(value.Game.BggId))
                    .Select(x => new EffectiveCampExpansion(x.BggId, x.Name,
                        x.Snapshot.OriginalName, x.Providers, x.Snapshot.MinPlayers, x.Snapshot.MaxPlayers)))
                .GroupBy(x => x.BggId)
                .Select(group => new EffectiveCampExpansion(group.Key,
                    group.Select(x => x.Name).First(x => !string.IsNullOrWhiteSpace(x)),
                    group.Select(x => x.OriginalName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    group.SelectMany(x => x.Providers).DistinctBy(x => x.ParticipantId).ToArray(),
                    group.Select(x => x.MinPlayers).FirstOrDefault(x => x.HasValue),
                    group.Select(x => x.MaxPlayers).FirstOrDefault(x => x.HasValue)))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var game = value.Game with
            {
                Expansions = expansionMap.Select(x => new ClubCollectionExpansion(x.BggId, x.Name,
                    x.OriginalName, x.MinPlayers, x.MaxPlayers)).ToArray()
            };
            return new EffectiveCampGame(game, value.InBase, value.Providers, expansionMap);
        }).OrderBy(x => x.Game.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ClubCollectionGame ToGame(EffectiveCampCatalogItem value) =>
        value.Snapshot.ToCollectionGame(value.BggId);


}
