using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using Microsoft.EntityFrameworkCore;

namespace oyinQ.Bot.Features.Catalog;

public sealed record EffectiveCampExpansion(long BggId, string Name,
    IReadOnlyList<CampCatalogProvider> Providers);

public sealed record EffectiveCampGame(ClubCollectionGame Game, bool IsInBaseCollection,
    IReadOnlyList<CampCatalogProvider> Providers, IReadOnlyList<EffectiveCampExpansion> Expansions);

public sealed class EffectiveCampCatalogService(
    AppDbContext dbContext,
    CampContributionSelectionService contributions)
{
    public async Task<IReadOnlyList<EffectiveCampGame>> LoadAsync(string communityKey,
        long? currentParticipantId, CancellationToken cancellationToken)
    {
        var camp = await dbContext.Camps.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BotChatKey == communityKey, cancellationToken)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        var contributed = await contributions.GetEffectiveContributionsAsync(camp.Id, cancellationToken,
            currentParticipantId);
        return Build(camp.ReadBaseCollection(), contributed);
    }

    public static IReadOnlyList<EffectiveCampGame> Build(ClubCollectionDocument baseCollection,
        IReadOnlyCollection<EffectiveCampCatalogItem> contributions)
    {
        var bases = new List<(ClubCollectionGame Game, bool InBase, IReadOnlyList<CampCatalogProvider> Providers)>();
        foreach (var game in baseCollection.Games)
        {
            var personal = contributions.SingleOrDefault(x => x.ItemType == CampContributionItemType.BaseGame
                && x.BggId == game.BggId);
            bases.Add((personal is null ? game : MergeMetadata(game, personal.Snapshot), true,
                personal?.Providers ?? []));
        }
        bases.AddRange(contributions.Where(x => x.ItemType == CampContributionItemType.BaseGame
                && bases.All(existing => existing.Game.BggId != x.BggId))
            .Select(x => (ToGame(x), false, x.Providers)));

        var contributedExpansions = contributions.Where(x => x.ItemType == CampContributionItemType.Expansion)
            .ToArray();
        return bases.Select(value =>
        {
            var expansionMap = value.Game.Expansions
                .Select(x => new EffectiveCampExpansion(x.BggId, x.Name,
                    contributedExpansions.SingleOrDefault(c => c.BggId == x.BggId)?.Providers ?? []))
                .Concat(contributedExpansions.Where(x => x.ParentBggIds.Contains(value.Game.BggId))
                    .Select(x => new EffectiveCampExpansion(x.BggId, x.Name, x.Providers)))
                .GroupBy(x => x.BggId)
                .Select(group => new EffectiveCampExpansion(group.Key,
                    group.Select(x => x.Name).First(x => !string.IsNullOrWhiteSpace(x)),
                    group.SelectMany(x => x.Providers).DistinctBy(x => x.ParticipantId).ToArray()))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var game = value.Game with
            {
                Expansions = expansionMap.Select(x => new ClubCollectionExpansion(x.BggId, x.Name)).ToArray()
            };
            return new EffectiveCampGame(game, value.InBase, value.Providers, expansionMap);
        }).OrderBy(x => x.Game.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ClubCollectionGame ToGame(EffectiveCampCatalogItem value)
    {
        var s = value.Snapshot;
        return new ClubCollectionGame(value.BggId, s.Name, s.ThumbnailImageUrl, s.ImageUrl, s.MinPlayers,
            s.MaxPlayers, s.BestPlayers, [], s.Types, s.Categories, s.Description, s.YearPublished,
            s.MinPlayTimeMinutes, s.MaxPlayTimeMinutes, s.MinAge, s.Type, s.Subdomains, s.CategoryItems,
            s.Mechanics);
    }

    private static ClubCollectionGame MergeMetadata(ClubCollectionGame current,
        CampContributionSnapshot fallback) => current with
    {
        ThumbnailImageUrl = current.ThumbnailImageUrl ?? fallback.ThumbnailImageUrl,
        ImageUrl = current.ImageUrl ?? fallback.ImageUrl,
        Description = current.Description ?? fallback.Description,
        YearPublished = current.YearPublished ?? fallback.YearPublished,
        MinPlayers = current.MinPlayers ?? fallback.MinPlayers,
        MaxPlayers = current.MaxPlayers ?? fallback.MaxPlayers,
        BestPlayers = current.BestPlayers ?? fallback.BestPlayers,
        MinPlayTimeMinutes = current.MinPlayTimeMinutes ?? fallback.MinPlayTimeMinutes,
        MaxPlayTimeMinutes = current.MaxPlayTimeMinutes ?? fallback.MaxPlayTimeMinutes,
        MinAge = current.MinAge ?? fallback.MinAge,
        Type = current.Type == GameType.Other ? fallback.Type : current.Type,
        Types = current.Types is { Count: > 0 } ? current.Types : fallback.Types,
        Categories = current.Categories is { Count: > 0 } ? current.Categories : fallback.Categories,
        Subdomains = current.Subdomains is { Count: > 0 } ? current.Subdomains : fallback.Subdomains,
        CategoryItems = current.CategoryItems is { Count: > 0 } ? current.CategoryItems : fallback.CategoryItems,
        Mechanics = current.Mechanics is { Count: > 0 } ? current.Mechanics : fallback.Mechanics
    };
}
