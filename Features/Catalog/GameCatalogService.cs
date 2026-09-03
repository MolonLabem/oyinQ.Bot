using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Catalog;

public sealed record CatalogQuery(string? Search, int? Players, IReadOnlyCollection<GameType> Types,
    IReadOnlyCollection<long> CategoryIds, string? Sort);
public sealed record LocalizedTaxonomyItem(long BggId, string Name);
public sealed record GameListItemResponse(long BggId, string Name, string? OriginalName, string? ThumbnailImageUrl,
    GameType Type, string TypeName, IReadOnlyList<string> TypeNames,
    int? MinPlayers, int? MaxPlayers, string? BestPlayers,
    string AvailabilitySummary, bool IsDefinitelyAvailable,
    bool NeedsProviderCoordination);
public sealed record GameAvailabilityResponse(bool IsInBaseCollection, IReadOnlyList<CampCatalogProvider> Providers,
    bool HasCommittedProvider);
public sealed record GameDetailsResponse(long BggId, string Name, string? OriginalName, string? ImageUrl, string? Description,
    int? YearPublished, GameType Type, string TypeName, IReadOnlyList<string> TypeNames,
    int? MinPlayers, int? MaxPlayers, string? BestPlayers,
    int? MinPlayTimeMinutes, int? MaxPlayTimeMinutes, int? MinAge,
    IReadOnlyList<LocalizedTaxonomyItem> Categories, IReadOnlyList<LocalizedTaxonomyItem> Mechanics,
    IReadOnlyList<ClubCollectionExpansion> Expansions, string BggUrl, GameAvailabilityResponse Availability);
public sealed record CatalogFilterOptions(IReadOnlyList<LocalizedTaxonomyItem> Categories,
    IReadOnlyList<KeyValuePair<GameType, string>> Types);
public sealed record GameCatalogResponse(IReadOnlyList<GameListItemResponse> Items, CatalogFilterOptions Filters);

public sealed class GameNotInCollectionException(long bggId)
    : KeyNotFoundException($"Игра BGG {bggId} отсутствует в коллекции сообщества.")
{
    public long BggId { get; } = bggId;
}

public sealed class GameCatalogService(AppDbContext dbContext, EffectiveCampCatalogService campCatalog)
{
    public async Task<GameCatalogResponse> ListAsync(string communityKey, BotMode mode, long telegramUserId,
        CatalogQuery query, CancellationToken cancellationToken)
    {
        var effective = await LoadAsync(communityKey, mode, telegramUserId, cancellationToken);
        IEnumerable<EffectiveGame> filtered = effective;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(value => value.Game.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || value.Game.OriginalName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        }
        filtered = filtered.Where(value => Matches(value.Game, query));

        filtered = query.Sort?.ToLowerInvariant() switch
        {
            "players" => filtered.OrderBy(value => value.Game.MinPlayers ?? int.MaxValue).ThenBy(value => value.Game.Name),
            _ => filtered.OrderBy(value => value.Game.Name, StringComparer.OrdinalIgnoreCase)
        };
        var items = filtered.Select(ToListItem).ToArray();
        var categories = effective.SelectMany(value => value.Game.CategoryItems ?? [])
            .DistinctBy(value => value.BggId).OrderBy(value => BggTaxonomyCatalog.LocalizeCategory(value))
            .Select(value => new LocalizedTaxonomyItem(value.BggId, BggTaxonomyCatalog.LocalizeCategory(value))).ToArray();
        var types = effective.SelectMany(value => BggTaxonomyCatalog.ResolveTypes(value.Game.Type,
                value.Game.Subdomains, value.Game.Types, value.Game.CategoryItems, value.Game.Categories))
            .Distinct().Order()
            .Select(value => new KeyValuePair<GameType, string>(value, BggTaxonomyCatalog.DisplayName(value))).ToArray();
        return new GameCatalogResponse(items, new CatalogFilterOptions(categories, types));
    }

    public async Task<GameDetailsResponse> DetailsAsync(string communityKey, BotMode mode, long telegramUserId,
        long bggId, CancellationToken cancellationToken)
    {
        var value = (await LoadAsync(communityKey, mode, telegramUserId, cancellationToken))
            .SingleOrDefault(x => x.Game.BggId == bggId) ?? throw new GameNotInCollectionException(bggId);
        var game = value.Game;
        var presentation = BggTaxonomyCatalog.Present(game);
        return new GameDetailsResponse(game.BggId, game.Name, game.OriginalName,
            game.ImageUrl ?? game.ThumbnailImageUrl,
            game.Description, game.YearPublished, game.Type, presentation.TypeName, presentation.TypeNames,
            game.MinPlayers, game.MaxPlayers, game.BestPlayers, game.MinPlayTimeMinutes,
            game.MaxPlayTimeMinutes, game.MinAge,
            (game.CategoryItems ?? []).Select(x => new LocalizedTaxonomyItem(x.BggId, BggTaxonomyCatalog.LocalizeCategory(x))).ToArray(),
            (game.Mechanics ?? []).Select(x => new LocalizedTaxonomyItem(x.BggId, BggTaxonomyCatalog.LocalizeMechanic(x))).ToArray(),
            game.Expansions, BggGameUrl.FromId(game.BggId)!,
            new GameAvailabilityResponse(value.IsInBaseCollection, value.Providers,
                value.Providers.Any(x => x.Commitment == CampBringCommitment.Bringing)));
    }

    private async Task<IReadOnlyList<EffectiveGame>> LoadAsync(string key, BotMode mode, long telegramUserId,
        CancellationToken cancellationToken)
    {
        if (mode == BotMode.Club)
        {
            var json = await dbContext.Clubs.AsNoTracking().Where(x => x.BotChatKey == key)
                .Select(x => x.CollectionJson).SingleAsync(cancellationToken);
            return ClubCollectionSerializer.Deserialize(json).Games
                .Select(game => new EffectiveGame(game, true, [])).ToArray();
        }

        var participantId = await dbContext.Participants.Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        return (await campCatalog.LoadAsync(key, participantId, cancellationToken))
            .Select(x => new EffectiveGame(x.Game, x.IsInBaseCollection, x.Providers)).ToArray();
    }

    private static GameListItemResponse ToListItem(EffectiveGame value)
    {
        var committed = value.Providers.Any(x => x.Commitment == CampBringCommitment.Bringing);
        var coordination = !value.IsInBaseCollection && value.Providers.Count > 1 && !committed;
        var summary = value.IsInBaseCollection ? "Есть в коллекции клуба" : committed ? "Точно будет"
            : coordination ? "Нужно решить, кто привезёт" : value.Providers.Count > 0 ? "Можно привезти" : string.Empty;
        var presentation = BggTaxonomyCatalog.Present(value.Game);
        return new GameListItemResponse(value.Game.BggId, value.Game.Name, value.Game.OriginalName,
            value.Game.ThumbnailImageUrl,
            value.Game.Type, presentation.TypeName, presentation.TypeNames, value.Game.MinPlayers,
            value.Game.MaxPlayers, value.Game.BestPlayers, summary,
            value.IsInBaseCollection || committed, coordination);
    }

    public static bool Matches(ClubCollectionGame game, CatalogQuery query)
    {
        if (query.Players is { } players && !(game.MinPlayers <= players && game.MaxPlayers >= players)) return false;
        if (query.Types.Count > 0 && !BggTaxonomyCatalog.ResolveTypes(game.Type, game.Subdomains,
                game.Types, game.CategoryItems, game.Categories).Any(query.Types.Contains)) return false;
        return query.CategoryIds.Count == 0
            || query.CategoryIds.All(id => game.CategoryItems?.Any(x => x.BggId == id) == true);
    }

    private sealed record EffectiveGame(ClubCollectionGame Game, bool IsInBaseCollection,
        IReadOnlyList<CampCatalogProvider> Providers);
}
