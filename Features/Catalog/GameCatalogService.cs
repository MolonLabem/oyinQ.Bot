using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Catalog;

public sealed record CatalogQuery(string? Search, int? Players, IReadOnlyCollection<GameType> Types,
    IReadOnlyCollection<long> CategoryIds, string? Sort, string? Ownership = null, string? Availability = null,
    string? Planning = null, IReadOnlyCollection<long>? ProviderParticipantIds = null);
public sealed record LocalizedTaxonomyItem(long BggId, string Name);
public sealed record CatalogProviderFilter(long ParticipantId, string DisplayName);
public sealed record GameListItemResponse(long BggId, string Name, string? OriginalName, string? ThumbnailImageUrl,
    GameType Type, string TypeName, IReadOnlyList<string> TypeNames,
    int? MinPlayers, int? MaxPlayers, string? BestPlayers,
    string AvailabilitySummary, bool IsDefinitelyAvailable,
    bool NeedsProviderCoordination, int ScheduledGatherings = 0, int RecordedPlays = 0, bool IsWished = false, bool CanWish = true, IReadOnlyList<ClubCollectionExpansion>? Expansions = null);
public sealed record GameAvailabilityResponse(bool IsInBaseCollection, IReadOnlyList<CampCatalogProvider> Providers,
    bool HasCommittedProvider, bool IsOwned = false);
public sealed record GameDetailsResponse(long BggId, string Name, string? OriginalName, string? ImageUrl, string? Description,
    int? YearPublished, GameType Type, string TypeName, IReadOnlyList<string> TypeNames,
    int? MinPlayers, int? MaxPlayers, string? BestPlayers,
    int? MinPlayTimeMinutes, int? MaxPlayTimeMinutes, int? MinAge,
    IReadOnlyList<LocalizedTaxonomyItem> Categories, IReadOnlyList<LocalizedTaxonomyItem> Mechanics,
    IReadOnlyList<ClubCollectionExpansion> Expansions, string BggUrl, GameAvailabilityResponse Availability, bool IsWished = false, bool CanWish = true);
public sealed record CatalogFilterOptions(IReadOnlyList<LocalizedTaxonomyItem> Categories,
    IReadOnlyList<KeyValuePair<GameType, string>> Types, IReadOnlyList<CatalogProviderFilter> Providers);
public sealed record GameCatalogResponse(IReadOnlyList<GameListItemResponse> Items, CatalogFilterOptions Filters);

public sealed class GameNotInCollectionException(long bggId)
    : KeyNotFoundException($"Игра BGG {bggId} отсутствует в коллекции сообщества.")
{
    public long BggId { get; } = bggId;
}

public sealed class GameCatalogService(AppDbContext dbContext, EffectiveCampCatalogService campCatalog, TimeProvider? timeProvider = null)
{
    public async Task<GameCatalogResponse> ListAsync(string communityKey, BotMode mode, long telegramUserId,
        CatalogQuery query, CancellationToken cancellationToken)
    {
        var effective = await LoadAsync(communityKey, mode, telegramUserId, cancellationToken);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var plannedSnapshots = await dbContext.GameGatherings.AsNoTracking().Where(x => x.CommunityKey == communityKey
            && x.StartsAtUtc > now && Features.Gatherings.GatheringLifecycle.ScheduledStatuses.Contains(x.Status))
            .Select(x => x.GameSnapshotJson).ToArrayAsync(cancellationToken);
        var playedSnapshots = await dbContext.GatheringPlayRecords.AsNoTracking().Where(x => x.WasPlayed && x.Gathering.CommunityKey == communityKey)
            .Select(x => x.GameSnapshotJson).ToArrayAsync(cancellationToken);
        var planned = plannedSnapshots.Select(x => Features.Gatherings.GatheringGameSnapshotSerializer.Deserialize(x).BggId)
            .Where(x => x.HasValue).GroupBy(x => x!.Value).ToDictionary(x => x.Key, x => x.Count());
        var played = playedSnapshots.Select(x => Features.Gatherings.GatheringGameSnapshotSerializer.Deserialize(x).BggId)
            .Where(x => x.HasValue).GroupBy(x => x!.Value).ToDictionary(x => x.Key, x => x.Count());
        IEnumerable<EffectiveGame> filtered = effective;
        filtered = query.Ownership switch
        {
            "club" => filtered.Where(x => x.IsInBaseCollection),
            "mine" => filtered.Where(x => x.IsOwned),
            "wishes" => filtered.Where(x => x.IsWished),
            "participants" => filtered.Where(x => x.Providers.Count > 0),
            _ => filtered
        };
        if (query.ProviderParticipantIds is { Count: > 0 } providerParticipantIds)
            filtered = filtered.Where(x => x.Providers.Any(provider => provider.ParticipantId is { } participantId
                && providerParticipantIds.Contains(participantId)));
        filtered = query.Availability switch
        {
            "confirmed" => filtered.Where(x => GameProviderService.Describe(x.IsInBaseCollection, x.Providers).IsConfirmed),
            "possible" => filtered.Where(x => !GameProviderService.Describe(x.IsInBaseCollection, x.Providers).IsConfirmed && x.Providers.Count > 0),
            _ => filtered
        };
        filtered = query.Planning switch
        {
            "planned" => filtered.Where(x => planned.ContainsKey(x.Game.BggId)),
            "unplanned" => filtered.Where(x => !planned.ContainsKey(x.Game.BggId)),
            _ => filtered
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(value => value.Game.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || value.Game.OriginalName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
                || value.Game.Expansions.Any(expansion => expansion.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || expansion.OriginalName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
        }
        filtered = filtered.Where(value => Matches(value.Game, query));

        filtered = query.Sort?.ToLowerInvariant() switch
        {
            "popular" => filtered.OrderByDescending(value => played.GetValueOrDefault(value.Game.BggId)).ThenBy(value => value.Game.Name).ThenBy(value => value.Game.BggId),
            "players" => filtered.OrderBy(value => value.Game.MinPlayers ?? int.MaxValue).ThenBy(value => value.Game.Name).ThenBy(value => value.Game.BggId),
            _ => filtered.OrderBy(value => value.Game.Name, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Game.BggId)
        };
        var items = filtered.Select(x => ToListItem(x) with { ScheduledGatherings = planned.GetValueOrDefault(x.Game.BggId), RecordedPlays = played.GetValueOrDefault(x.Game.BggId) }).ToArray();
        var categories = effective.SelectMany(value => value.Game.CategoryItems ?? [])
            .DistinctBy(value => value.BggId).OrderBy(value => BggTaxonomyCatalog.LocalizeCategory(value))
            .Select(value => new LocalizedTaxonomyItem(value.BggId, BggTaxonomyCatalog.LocalizeCategory(value))).ToArray();
        var types = effective.SelectMany(value => BggTaxonomyCatalog.ResolveTypes(value.Game.Type,
                value.Game.Subdomains, value.Game.Types, value.Game.CategoryItems, value.Game.Categories))
            .Distinct().Order()
            .Select(value => new KeyValuePair<GameType, string>(value, BggTaxonomyCatalog.DisplayName(value))).ToArray();
        var providerFilters = effective.SelectMany(value => value.Providers)
            .Where(value => value.ParticipantId.HasValue)
            .DistinctBy(value => value.ParticipantId)
            .OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.ParticipantId)
            .Select(value => new CatalogProviderFilter(value.ParticipantId!.Value, value.DisplayName))
            .ToArray();
        return new GameCatalogResponse(items, new CatalogFilterOptions(categories, types, providerFilters));
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
                GameProviderService.Describe(false, value.Providers).IsConfirmed, value.IsOwned), value.IsWished, value.IsBaseGame);
    }

    public async Task<IReadOnlyList<EffectiveGame>> LoadAsync(string key, BotMode mode, long telegramUserId,
        CancellationToken cancellationToken)
    {
        if (mode == BotMode.Club)
        {
            return await LoadClubAsync(key, telegramUserId, cancellationToken);
        }

        var participantId = await dbContext.Participants.Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var owned = await dbContext.ParticipantCollectionItems.Where(x => x.ParticipantId == participantId).Select(x => x.BggId).ToArrayAsync(cancellationToken);
        var games = (await campCatalog.LoadAsync(key, participantId, cancellationToken))
            .Select(x => new EffectiveGame(x.Game, x.IsInBaseCollection, x.Providers, owned.Contains(x.Game.BggId))).ToArray();
        return await WithWishesAsync(games, key, telegramUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<EffectiveGame>> LoadClubAsync(string key, long telegramUserId,
        CancellationToken cancellationToken)
    {
        var json = await dbContext.Clubs.AsNoTracking().Where(x => x.BotChatKey == key)
            .Select(x => x.CollectionJson).SingleAsync(cancellationToken);
        var personal = await dbContext.ParticipantCollectionItems.AsNoTracking()
            .Where(x => x.Participant.TelegramUserId == telegramUserId).ToArrayAsync(cancellationToken);
        var ownedIds = personal.Select(x => x.BggId).ToHashSet();
        var document = ClubCollectionSerializer.Deserialize(json);
        var clubOwnedIds = document.Games.Select(x => x.BggId)
            .Concat(document.Games.SelectMany(x => x.Expansions).Select(x => x.BggId)).ToHashSet();
        var games = document.Games
            .Select(x => new EffectiveGame(x, true, [], ownedIds.Contains(x.BggId))).ToList();
        var personalSnapshots = personal.Select(x => (Item: x, Snapshot: x.ReadSnapshot())).ToArray();
        foreach (var (item, snapshot) in personalSnapshots.Where(x => games.All(g => g.Game.BggId != x.Item.BggId)))
            games.Add(new(snapshot.ToCollectionGame(item.BggId), clubOwnedIds.Contains(item.BggId), [], true, IsBaseGame: item.ItemType == CollectionItemType.BaseGame));
        var expansionsByParent = personalSnapshots.Where(x => x.Item.ItemType == CollectionItemType.Expansion)
            .SelectMany(x => (x.Snapshot.ParentBggIds ?? (x.Item.ParentBggId is { } parent ? [parent] : []))
                .Select(parent => (Parent: parent, Expansion: new ClubCollectionExpansion(x.Item.BggId, x.Snapshot.Name, x.Snapshot.OriginalName))))
            .ToLookup(x => x.Parent, x => x.Expansion);
        var merged = games.Select(value => value with { Game = value.Game with
        {
            Expansions = value.Game.Expansions.Concat(expansionsByParent[value.Game.BggId]).DistinctBy(x => x.BggId).ToArray()
        }}).ToArray();
        return await WithWishesAsync(merged, key, telegramUserId, cancellationToken);
    }

    private async Task<IReadOnlyList<EffectiveGame>> WithWishesAsync(IEnumerable<EffectiveGame> games, string key, long telegramUserId, CancellationToken ct)
    {
        var wishes = await dbContext.GameWishes.AsNoTracking().Where(x => x.CommunityKey == key
            && x.Participant.TelegramUserId == telegramUserId).ToArrayAsync(ct);
        var result = games.Select(x => x with { IsWished = wishes.Any(w => w.BggId == x.Game.BggId) }).ToList();
        foreach (var wish in wishes.Where(x => result.All(g => g.Game.BggId != x.BggId)))
            result.Add(new(ClubCollectionSerializer.Deserialize(wish.SnapshotJson).Games.Single(), false, [], IsWished: true));
        return result;
    }

    private static GameListItemResponse ToListItem(EffectiveGame value)
    {
        var provider = GameProviderService.Describe(value.IsInBaseCollection, value.Providers);
        var committed = provider.IsConfirmed;
        var coordination = !provider.IsConfirmed;
        var summary = value.IsOwned ? (value.IsInBaseCollection ? "Есть в клубе · Есть у вас" : "Есть у вас")
            : value.IsInBaseCollection ? "Есть в клубе" : committed ? "Точно будет"
            : provider.Summary;
        var presentation = BggTaxonomyCatalog.Present(value.Game);
        return new GameListItemResponse(value.Game.BggId, value.Game.Name, value.Game.OriginalName,
            value.Game.ThumbnailImageUrl,
            value.Game.Type, presentation.TypeName, presentation.TypeNames, value.Game.MinPlayers,
            value.Game.MaxPlayers, value.Game.BestPlayers, summary,
            value.IsInBaseCollection || committed, coordination, IsWished: value.IsWished, CanWish: value.IsBaseGame, Expansions: value.Game.Expansions);
    }

    public static bool Matches(ClubCollectionGame game, CatalogQuery query)
    {
        if (query.Players is { } players && !(game.MinPlayers <= players && game.MaxPlayers >= players)) return false;
        if (query.Types.Count > 0 && !BggTaxonomyCatalog.ResolveTypes(game.Type, game.Subdomains,
                game.Types, game.CategoryItems, game.Categories).Any(query.Types.Contains)) return false;
        return query.CategoryIds.Count == 0
            || query.CategoryIds.All(id => game.CategoryItems?.Any(x => x.BggId == id) == true);
    }

    public sealed record EffectiveGame(ClubCollectionGame Game, bool IsInBaseCollection,
        IReadOnlyList<CampCatalogProvider> Providers, bool IsOwned = false, bool IsWished = false, bool IsBaseGame = true);
}
