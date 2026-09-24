using oyinQ.Bot.Features.Gatherings;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Catalog;

public enum CatalogPlayerCountMode { Supported, Best }

public sealed record CatalogQuery(string? Search, int? Players, IReadOnlyCollection<GameType> Types,
    IReadOnlyCollection<long> CategoryIds, string? Sort, string? Ownership = null, string? Availability = null,
    string? Planning = null, IReadOnlyCollection<long>? ProviderParticipantIds = null,
    IReadOnlyCollection<GameComplexity>? ComplexityLevels = null, int? MaxDurationMinutes = null,
    IReadOnlyCollection<long>? MechanicIds = null, DateOnly? AttendanceDate = null,
    int? FromYear = null, int? ToYear = null, CatalogPlayerCountMode PlayerCountMode = CatalogPlayerCountMode.Supported);
public sealed record LocalizedTaxonomyItem(long BggId, string Name);
public sealed record CatalogProviderFilter(long ParticipantId, string DisplayName);
public sealed record GameListItemResponse(long BggId, string Name, string? OriginalName, string? ThumbnailImageUrl,
    GameType Type, string TypeName, IReadOnlyList<string> TypeNames,
    int? MinPlayers, int? MaxPlayers, string? BestPlayers,
    string AvailabilitySummary, bool IsDefinitelyAvailable,
    bool NeedsProviderCoordination, int ScheduledGatherings = 0, int RecordedPlays = 0, bool IsWished = false, bool CanWish = true, IReadOnlyList<ClubCollectionExpansion>? Expansions = null, PlayerCountRange? ExpansionPlayerRange = null, ComplexityInfo? ComplexityInfo = null);
public sealed record GameAvailabilityResponse(bool IsInBaseCollection, IReadOnlyList<CampCatalogProvider> Providers,
    bool HasCommittedProvider, bool IsOwned = false);
public sealed record GameDetailsResponse(long BggId, string Name, string? OriginalName, string? ImageUrl, string? Description,
    int? YearPublished, GameType Type, string TypeName, IReadOnlyList<string> TypeNames,
    int? MinPlayers, int? MaxPlayers, string? BestPlayers,
    int? MinPlayTimeMinutes, int? MaxPlayTimeMinutes, int? MinAge,
    IReadOnlyList<LocalizedTaxonomyItem> Categories, IReadOnlyList<LocalizedTaxonomyItem> Mechanics,
    IReadOnlyList<ClubCollectionExpansion> Expansions, string BggUrl, GameAvailabilityResponse Availability, bool IsWished = false, bool CanWish = true,
    int ScheduledGatherings = 0, int RecordedPlays = 0, PlayerCountRange? ExpansionPlayerRange = null, ComplexityInfo? ComplexityInfo = null);
public sealed record CatalogFilterOptions(IReadOnlyList<LocalizedTaxonomyItem> Categories,
    IReadOnlyList<KeyValuePair<GameType, string>> Types, IReadOnlyList<CatalogProviderFilter> Providers,
    IReadOnlyList<LocalizedTaxonomyItem>? Mechanics = null, IReadOnlyList<ComplexityInfo>? Complexities = null);
public sealed record GameCatalogResponse(IReadOnlyList<GameListItemResponse> Items, CatalogFilterOptions Filters, int Total = 0);
public sealed record GameDemand(ClubCollectionGame Game, int InterestedParticipants, int ScheduledGatherings, string AvailabilitySummary);

public sealed class GameNotInCollectionException(long bggId)
    : KeyNotFoundException($"Игра BGG {bggId} отсутствует в коллекции сообщества.")
{
    public long BggId { get; } = bggId;
}

public sealed class GameCatalogService(AppDbContext dbContext, EffectiveCampCatalogService campCatalog, TimeProvider? timeProvider = null)
{
    public async Task<IReadOnlyList<GameDemand>> DemandAsync(string key, BotMode mode, long telegramUserId, CancellationToken ct)
    {
        var wishes = await dbContext.GameWishes.AsNoTracking().Where(x => x.CommunityKey == key).OrderBy(x => x.CreatedAt).ThenBy(x => x.ParticipantId).ToArrayAsync(ct);
        var effective = (await LoadAsync(key, mode, telegramUserId, ct)).Where(x => x.IsBaseGame).ToDictionary(x => x.Game.BggId);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var snapshots = await GatheringListQuery.Apply(dbContext.GameGatherings.AsNoTracking().Where(x => x.CommunityKey == key), GatheringListScope.Upcoming, now)
            .Select(x => x.GameSnapshotJson).ToArrayAsync(ct);
        var planned = snapshots.Select(x => GatheringGameSnapshotSerializer.Deserialize(x).BggId).OfType<long>().GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        return wishes.GroupBy(x => x.BggId).Select(group =>
        {
            var available = effective.GetValueOrDefault(group.Key);
            var game = available?.Game ?? ClubCollectionSerializer.Deserialize(group.Last().SnapshotJson).Games.Single() with { Expansions = [] };
            return new GameDemand(game, group.Select(x => x.ParticipantId).Distinct().Count(), planned.GetValueOrDefault(group.Key),
                available is null ? "Коробка пока не подтверждена" : GameProviderService.Describe(mode == BotMode.Club && available.IsInBaseCollection, available.Providers).Summary);
        }).OrderBy(x => x.ScheduledGatherings > 0).ThenByDescending(x => x.InterestedParticipants).ThenBy(x => x.Game.Name).ThenBy(x => x.Game.BggId).ToArray();
    }

    public async Task<ClubCollectionGame> DemandGameAsync(string key, BotMode mode, long telegramUserId, long bggId, CancellationToken ct)
    {
        if (mode == BotMode.Camp)
        {
            var scope = await dbContext.Camps.AsNoTracking().Include(x => x.BotChat).SingleAsync(x => x.BotChatKey == key, ct);
            var registration = await dbContext.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays)
                .SingleOrDefaultAsync(x => x.CampId == scope.Id && x.Participant.TelegramUserId == telegramUserId, ct);
            if (!CampParticipationPolicy.IsRegistrationComplete(registration, scope))
                throw new UnauthorizedAccessException("Сначала завершите регистрацию на этот кэмп.");
        }
        var available = (await LoadAsync(key, mode, telegramUserId, ct)).SingleOrDefault(x => x.Game.BggId == bggId && x.IsBaseGame);
        if (available is not null) return available.Game;
        var wish = await dbContext.GameWishes.AsNoTracking().Where(x => x.CommunityKey == key && x.BggId == bggId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.ParticipantId).FirstOrDefaultAsync(ct);
        if (wish != null) return ClubCollectionSerializer.Deserialize(wish.SnapshotJson).Games.Single() with { Expansions = [] };
        if (mode == BotMode.Camp)
        {
            var camp = await dbContext.Camps.AsNoTracking().Include(x => x.BotChat).SingleAsync(x => x.BotChatKey == key, ct);
            var registrations = await dbContext.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays)
                .Where(x => x.CampId == camp.Id && !dbContext.CampParticipantVisibilities.Any(v => v.CampId == camp.Id
                    && v.ParticipantId == x.ParticipantId && !v.ShareCollection)).ToArrayAsync(ct);
            var ids = registrations.Where(x => CampParticipationPolicy.IsRegistrationComplete(x, camp)).Select(x => x.ParticipantId).ToArray();
            var visible = await dbContext.ParticipantCollectionItems.AsNoTracking().Where(x => x.BggId == bggId && x.ItemType == CollectionItemType.BaseGame
                && (x.Participant.TelegramUserId == telegramUserId || ids.Contains(x.ParticipantId))).OrderBy(x => x.ParticipantId).FirstOrDefaultAsync(ct);
            if (visible != null) return visible.ReadSnapshot().ToCollectionGame(bggId) with { Expansions = [] };
            var requested = await dbContext.CampBringRequests.AsNoTracking().Where(x => x.CampId == camp.Id && x.BggId == bggId
                && (x.Owner.TelegramUserId == telegramUserId || x.Requesters.Any(r => r.Participant.TelegramUserId == telegramUserId)))
                .OrderBy(x => x.Id).Select(x => x.SnapshotJson).FirstOrDefaultAsync(ct);
            if (requested != null) return CollectionItemSnapshotSerializer.Deserialize(requested).ToCollectionGame(bggId) with { Expansions = [] };
        }
        throw new KeyNotFoundException("Игра больше не представлена в спросе сообщества. Выберите игру заново.");
    }

    public async Task<GameCatalogResponse> ListAsync(string communityKey, BotMode mode, long telegramUserId,
        CatalogQuery query, CancellationToken cancellationToken, bool countOnly = false)
    {
        if (query.MaxDurationMinutes is <= 0 or > 10080) throw new ArgumentException("Укажите длительность от 1 до 10080 минут.");
        if (query.FromYear is < 1 or > 9999 || query.ToYear is < 1 or > 9999)
            throw new ArgumentException("Укажите целый год от 1 до 9999.");
        if (query.FromYear > query.ToYear) throw new ArgumentException("Год «От» не должен быть позже года «До».");
        if (!Enum.IsDefined(query.PlayerCountMode)) throw new ArgumentException("Неизвестный режим количества игроков.");
        query = NormalizeQuery(query, mode);
        var effective = await LoadAsync(communityKey, mode, telegramUserId, cancellationToken, query.AttendanceDate);
        // Restored filters may reference options removed by a source collection update.
        var categoryIds = effective.SelectMany(x => x.Game.CategoryItems ?? []).Select(x => x.BggId).ToHashSet();
        var providerIds = effective.SelectMany(x => x.Providers).Select(x => x.ParticipantId).ToHashSet();
        var availableTypes = effective.SelectMany(x => BggTaxonomyCatalog.ResolveTypes(x.Game.Type,
            x.Game.Subdomains, x.Game.Types, x.Game.CategoryItems, x.Game.Categories)).ToHashSet();
        query = query with {
            CategoryIds = query.CategoryIds.Where(categoryIds.Contains).Distinct().ToArray(),
            Types = query.Types.Where(availableTypes.Contains).Distinct().ToArray(),
            ProviderParticipantIds = query.ProviderParticipantIds?.Where(x => providerIds.Contains(x)).Distinct().ToArray()
        };
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var plannedSnapshots = await dbContext.GameGatherings.AsNoTracking().Where(x => x.CommunityKey == communityKey
            && x.StartsAtUtc > now && Features.Gatherings.GatheringLifecycle.ScheduledStatuses.Contains(x.Status))
            .Select(x => x.GameSnapshotJson).ToArrayAsync(cancellationToken);
        var playedSnapshots = countOnly ? [] : await dbContext.GatheringPlayRecords.AsNoTracking().Where(x => x.WasPlayed && x.Gathering.CommunityKey == communityKey)
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
            "confirmed" => filtered.Where(x => GameProviderService.Describe(false, x.Providers).IsConfirmed),
            "possible" => filtered.Where(x => !GameProviderService.Describe(false, x.Providers).IsConfirmed && x.Providers.Count > 0),
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

        var matched = filtered.ToArray();
        var nestedIds = matched.SelectMany(x => x.Game.Expansions.Where(e => e.BggId != x.Game.BggId)).Select(x => x.BggId).ToHashSet();
        var total = matched.Count(x => !nestedIds.Contains(x.Game.BggId));
        if (countOnly) return new([], new([], [], []), total);
        filtered = matched;

        filtered = query.Sort?.ToLowerInvariant() switch
        {
            "popular" => filtered.OrderByDescending(value => played.GetValueOrDefault(value.Game.BggId)).ThenBy(value => value.Game.Name).ThenBy(value => value.Game.BggId),
            "players" => filtered.OrderBy(value => value.Game.MinPlayers ?? int.MaxValue).ThenBy(value => value.Game.Name).ThenBy(value => value.Game.BggId),
            _ => filtered.OrderBy(value => value.Game.Name, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Game.BggId)
        };
        var items = filtered.Select(x => ToListItem(x, mode) with { ScheduledGatherings = planned.GetValueOrDefault(x.Game.BggId), RecordedPlays = played.GetValueOrDefault(x.Game.BggId) }).ToArray();
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
        var mechanics = effective.SelectMany(x => x.Game.Mechanics ?? []).DistinctBy(x => x.BggId)
            .Select(x => new LocalizedTaxonomyItem(x.BggId, BggTaxonomyCatalog.LocalizeMechanic(x))).OrderBy(x => x.Name).ToArray();
        var complexities = effective.Select(x => GameComplexityPresentation.Present(x.Game)).OfType<ComplexityInfo>()
            .DistinctBy(x => x.Level).OrderBy(x => x.Level).ToArray();
        return new GameCatalogResponse(items, new CatalogFilterOptions(categories, types, providerFilters, mechanics, complexities), total);
    }

    public async Task<GameDetailsResponse> DetailsAsync(string communityKey, BotMode mode, long telegramUserId,
        long bggId, CancellationToken cancellationToken, DateOnly? attendanceDate = null)
    {
        var value = (await LoadAsync(communityKey, mode, telegramUserId, cancellationToken, attendanceDate))
            .SingleOrDefault(x => x.Game.BggId == bggId) ?? throw new GameNotInCollectionException(bggId);
        var game = value.Game;
        var presentation = BggTaxonomyCatalog.Present(game);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var plannedSnapshots = await dbContext.GameGatherings.AsNoTracking().Where(x => x.CommunityKey == communityKey
            && x.StartsAtUtc > now && Features.Gatherings.GatheringLifecycle.ScheduledStatuses.Contains(x.Status))
            .Select(x => x.GameSnapshotJson).ToArrayAsync(cancellationToken);
        var playedSnapshots = await dbContext.GatheringPlayRecords.AsNoTracking().Where(x => x.WasPlayed && x.Gathering.CommunityKey == communityKey)
            .Select(x => x.GameSnapshotJson).ToArrayAsync(cancellationToken);
        var scheduledGatherings = plannedSnapshots.Count(x => Features.Gatherings.GatheringGameSnapshotSerializer.Deserialize(x).BggId == bggId);
        var recordedPlays = playedSnapshots.Count(x => Features.Gatherings.GatheringGameSnapshotSerializer.Deserialize(x).BggId == bggId);
        return new GameDetailsResponse(game.BggId, game.Name, game.OriginalName,
            game.ImageUrl ?? game.ThumbnailImageUrl,
            game.Description, game.YearPublished, game.Type, presentation.TypeName, presentation.TypeNames,
            game.MinPlayers, game.MaxPlayers, game.BestPlayers, game.MinPlayTimeMinutes,
            game.MaxPlayTimeMinutes, game.MinAge,
            (game.CategoryItems ?? []).Select(x => new LocalizedTaxonomyItem(x.BggId, BggTaxonomyCatalog.LocalizeCategory(x))).ToArray(),
            (game.Mechanics ?? []).Select(x => new LocalizedTaxonomyItem(x.BggId, BggTaxonomyCatalog.LocalizeMechanic(x))).ToArray(),
            game.Expansions, BggGameUrl.FromId(game.BggId)!,
            new GameAvailabilityResponse(value.IsInBaseCollection, value.Providers,
                GameProviderService.Describe(false, value.Providers).IsConfirmed, value.IsOwned), value.IsWished, value.IsBaseGame,
            scheduledGatherings, recordedPlays, ExpansionRange(game), GameComplexityPresentation.Present(game));
    }

    public async Task<IReadOnlyList<EffectiveGame>> LoadAsync(string key, BotMode mode, long telegramUserId,
        CancellationToken cancellationToken, DateOnly? attendanceDate = null)
    {
        if (mode == BotMode.Club)
        {
            return await LoadClubAsync(key, telegramUserId, cancellationToken);
        }

        if (attendanceDate is { } day)
        {
            var camp = await dbContext.Camps.AsNoTracking().Include(x => x.BotChat).SingleAsync(x => x.BotChatKey == key, cancellationToken);
            if (camp.StartDate is null || camp.EndDate is null || day < camp.StartDate || day > camp.EndDate)
                throw new ArgumentException("Выберите день в пределах дат кэмпа.");
        }

        var participantId = await dbContext.Participants.Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var owned = await dbContext.ParticipantCollectionItems.Where(x => x.ParticipantId == participantId).Select(x => x.BggId).ToArrayAsync(cancellationToken);
        var games = (await campCatalog.LoadAsync(key, participantId, cancellationToken, attendanceDate))
            .Select(x => new EffectiveGame(x.Game, x.IsInBaseCollection, x.Providers, owned.Contains(x.Game.BggId))).ToArray();
        return await WithWishesAsync(games, key, telegramUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<EffectiveGame>> LoadClubAsync(string key, long telegramUserId,
        CancellationToken cancellationToken)
    {
        var clubId = await dbContext.Clubs.AsNoTracking().Where(x => x.BotChatKey == key)
            .Select(x => x.Id).SingleAsync(cancellationToken);
        var source = await new SharedCollectionReader(dbContext).SourceAsync(clubId, cancellationToken);
        var personal = await dbContext.ParticipantCollectionItems.AsNoTracking()
            .Where(x => x.Participant.TelegramUserId == telegramUserId).ToArrayAsync(cancellationToken);
        var ownedIds = personal.Select(x => x.BggId).ToHashSet();
        var document = source.ReadCollection();
        var clubOwnedIds = document.Games.Select(x => x.BggId)
            .Concat(document.Games.SelectMany(x => x.Expansions).Select(x => x.BggId)).ToHashSet();
        var personalSnapshots = personal.Select(x => (Item: x, Snapshot: x.ReadSnapshot())).ToArray();
        var games = document.Games.Select(game =>
        {
            var fallback = personalSnapshots.SingleOrDefault(x => x.Item.BggId == game.BggId && x.Item.ItemType == CollectionItemType.BaseGame).Snapshot;
            return new EffectiveGame(fallback is null ? game : game.WithMetadataFallback(fallback), true, [], ownedIds.Contains(game.BggId));
        }).ToList();
        foreach (var (item, snapshot) in personalSnapshots.Where(x => games.All(g => g.Game.BggId != x.Item.BggId)))
            games.Add(new(snapshot.ToCollectionGame(item.BggId), clubOwnedIds.Contains(item.BggId), [], true, IsBaseGame: item.ItemType == CollectionItemType.BaseGame));
        var expansionsByParent = personalSnapshots.Where(x => x.Item.ItemType == CollectionItemType.Expansion)
            .SelectMany(x => (x.Snapshot.ParentBggIds ?? (x.Item.ParentBggId is { } parent ? [parent] : []))
                .Select(parent => (Parent: parent, Expansion: x.Snapshot.ToExpansion(x.Item.BggId))))
            .ToLookup(x => x.Parent, x => x.Expansion);
        var merged = games.Select(value => value with { Game = value.Game with
        {
            Expansions = ClubCollectionExpansion.Merge(value.Game.Expansions, expansionsByParent[value.Game.BggId])
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

    private static GameListItemResponse ToListItem(EffectiveGame value, BotMode mode)
    {
        var provider = GameProviderService.Describe(mode == BotMode.Club && value.IsInBaseCollection, value.Providers);
        var committed = provider.IsConfirmed;
        var coordination = !provider.IsConfirmed && value.Providers.Count > 1;
        var summary = value.IsOwned ? (value.IsInBaseCollection ? "Есть в клубе · Есть у вас" : "Есть у вас")
            : value.IsInBaseCollection && mode == BotMode.Club ? "Есть в клубе" : committed ? "Точно будет"
            : provider.Summary;
        var presentation = BggTaxonomyCatalog.Present(value.Game);
        return new GameListItemResponse(value.Game.BggId, value.Game.Name, value.Game.OriginalName,
            value.Game.ThumbnailImageUrl,
            value.Game.Type, presentation.TypeName, presentation.TypeNames, value.Game.MinPlayers,
            value.Game.MaxPlayers, value.Game.BestPlayers, summary,
            committed, coordination, IsWished: value.IsWished, CanWish: value.IsBaseGame, Expansions: value.Game.Expansions, ExpansionPlayerRange: ExpansionRange(value.Game), ComplexityInfo: GameComplexityPresentation.Present(value.Game));
    }

    public static CatalogQuery NormalizeQuery(CatalogQuery query, BotMode mode) => mode == BotMode.Club
        ? query with { Availability = null, ProviderParticipantIds = [], Ownership = query.Ownership == "participants" ? null : query.Ownership }
        : query;

    public static PlayerCountRange? ExpansionRange(ClubCollectionGame game)
    {
        var basic = PlayerCountRange.Normalize(game.MinPlayers, game.MaxPlayers);
        var expanded = basic.WithExpansions(game.Expansions);
        return !basic.WasDefaulted && expanded != basic ? expanded : null;
    }

    public static bool Matches(ClubCollectionGame game, CatalogQuery query)
    {
        if (query.FromYear.HasValue || query.ToYear.HasValue)
        {
            if (game.YearPublished is not { } year || year < query.FromYear || year > query.ToYear) return false;
        }
        if (query.ComplexityLevels is { Count: > 0 } levels
            && (GameComplexityPresentation.Resolve(game.ComplexityWeight, game.Complexity) is not { } level || !levels.Contains(level))) return false;
        if (query.MaxDurationMinutes is { } maximum)
        {
            var duration = game.MaxPlayTimeMinutes is > 0 ? game.MaxPlayTimeMinutes : game.MinPlayTimeMinutes;
            if (duration is not > 0 || duration > maximum) return false;
        }
        if (query.MechanicIds is { Count: > 0 } mechanics && !mechanics.All(id => game.Mechanics?.Any(x => x.BggId == id) == true)) return false;
        if (query.Players is { } players)
        {
            if (query.PlayerCountMode == CatalogPlayerCountMode.Best)
            {
                if (!BggBestPlayerRecommendation.Matches(game.BestPlayers, players)) return false;
            }
            else
            {
                var range = PlayerCountRange.Normalize(game.MinPlayers, game.MaxPlayers).WithExpansions(game.Expansions);
                if (range.WasDefaulted || players < range.Minimum || players > range.Maximum) return false;
            }
        }
        if (query.Types.Count > 0 && !BggTaxonomyCatalog.ResolveTypes(game.Type, game.Subdomains,
                game.Types, game.CategoryItems, game.Categories).Any(query.Types.Contains)) return false;
        return query.CategoryIds.Count == 0
            || query.CategoryIds.All(id => game.CategoryItems?.Any(x => x.BggId == id) == true);
    }

    public sealed record EffectiveGame(ClubCollectionGame Game, bool IsInBaseCollection,
        IReadOnlyList<CampCatalogProvider> Providers, bool IsOwned = false, bool IsWished = false, bool IsBaseGame = true);
}
