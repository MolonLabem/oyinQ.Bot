using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringGameSelectionService(
    AppDbContext dbContext,
    IBoardGameGeekClient bggClient,
    EffectiveCampCatalogService? campCatalog = null,
    ILogger<GatheringGameSelectionService>? logger = null)
{
    public async Task<GatheringGameSnapshot> EnrichExpansionMetadataAsync(GatheringGameSnapshot snapshot,
        CancellationToken ct, IReadOnlyCollection<long>? selected = null)
    {
        if (snapshot.BggId is { } baseId)
        {
            try
            {
                var details = await new BggSelectionService(bggClient).LoadBaseSelectionAsync(baseId,
                    (selected ?? []).Except((snapshot.KnownExpansions ?? snapshot.SelectedExpansions)
                        .Concat(snapshot.SelectedExpansions).Select(x => x.BggId)).ToArray(), ct);
                snapshot = ApplyExpansionMetadata(snapshot,
                    details.Expansions.Select(BggGameMapper.ToCollectionExpansion).ToArray());
            }
            catch (KeyNotFoundException) { /* Keep the saved, validated relationships. */ }
            catch (HttpRequestException) when (!ct.IsCancellationRequested) { /* Saved selections remain editable. */ }
        }
        var missing = (snapshot.KnownExpansions ?? snapshot.SelectedExpansions)
            .Where(item => (selected is null || selected.Contains(item.BggId))
                && PlayerCountRange.Normalize(item.MinPlayers, item.MaxPlayers).WasDefaulted)
            .Select(item => item.BggId).ToArray();
        if (snapshot.BggId is null || missing.Length == 0) return snapshot;
        try
        {
            var items = await bggClient.GetItemsByIdsAsync(missing, ct);
            var metadata = items.Where(item => item.IsExpansion && missing.Contains(item.Game.BggId ?? 0)
                    && item.ParentBggIds.Contains(snapshot.BggId.Value))
                .Select(item => BggGameMapper.ToCollectionExpansion(BggGameMapper.ToBggExpansion(item))).ToArray();
            return ApplyExpansionMetadata(snapshot, metadata);
        }
        catch (HttpRequestException exception) when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning(exception, "BGG expansion metadata unavailable for gathering game {BggId}.", snapshot.BggId);
            return snapshot;
        }
    }

    public static GatheringGameSnapshot ApplyExpansionMetadata(GatheringGameSnapshot snapshot,
        IReadOnlyList<ClubCollectionExpansion> metadata)
    {
        var known = ClubCollectionExpansion.Merge(
            (snapshot.KnownExpansions ?? snapshot.SelectedExpansions).Concat(snapshot.SelectedExpansions), metadata);
        return (snapshot with { KnownExpansions = known })
            .WithExpansions(snapshot.SelectedExpansions.Select(item => item.BggId).ToArray());
    }

    public async Task<IReadOnlyList<CampBggImportDraftItem>> ExpansionOwnershipAsync(
        GatheringGameSnapshot snapshot, IReadOnlyCollection<long> selected,
        IReadOnlyCollection<long> requested, CancellationToken ct)
    {
        if (requested.Except(selected).Any())
            throw new ArgumentException("Можно добавить только выбранные для сбора дополнения.");
        var items = GatheringExpansionSelection.Select(snapshot.KnownExpansions ?? snapshot.SelectedExpansions, requested);
        if (items.Count == 0) return [];
        if (snapshot.BggId is not { } baseId) throw new InvalidOperationException("У игры отсутствует BGG ID.");
        IReadOnlyList<BggCollectionItem> metadata = [];
        try { metadata = await bggClient.GetItemsByIdsAsync(requested.Distinct().ToArray(), ct); }
        catch (HttpRequestException) when (!ct.IsCancellationRequested) { /* Validated saved metadata is sufficient. */ }
        return items.Select(item =>
        {
            var full = metadata.SingleOrDefault(x => x.Game.BggId == item.BggId && x.IsExpansion && x.ParentBggIds.Contains(baseId));
            return full is not null ? BggGameMapper.ToDraftItem(full) : BggGameMapper.ToExpansionOwnership(baseId, item);
        }).ToArray();
    }

    public async Task<GatheringGameSnapshot> FromClubCollectionAsync(
        string communityKey, long bggId, IReadOnlyCollection<long> selectedExpansionIds,
        CancellationToken cancellationToken, long telegramUserId = 0)
    {
        var catalog = new GameCatalogService(dbContext, campCatalog!);
        var game = (await catalog.LoadClubAsync(communityKey, telegramUserId, cancellationToken))
            .SingleOrDefault(x => x.Game.BggId == bggId && x.IsBaseGame)?.Game
            ?? throw new KeyNotFoundException("Игра не найдена в доступной вам коллекции.");
        return await FromSavedGameAsync(game, selectedExpansionIds, cancellationToken);
    }

    public async Task<GatheringGameSnapshot> FromArbitraryBggAsync(long bggId,
        IReadOnlyCollection<long> selectedExpansionIds, CancellationToken cancellationToken) =>
        (await ExternalSelectionAsync(bggId, selectedExpansionIds, cancellationToken)).Snapshot;

    public async Task<(GatheringGameSnapshot Snapshot, IReadOnlyList<CampBggImportDraftItem> Ownership)> ExternalSelectionAsync(
        long bggId, IReadOnlyCollection<long> selectedExpansionIds, CancellationToken cancellationToken)
    {
        var details = await new BggSelectionService(bggClient).LoadBaseSelectionAsync(bggId, selectedExpansionIds, cancellationToken);
        var snapshot = FromCanonicalDetails(details, selectedExpansionIds, "bgg");
        if (snapshot.BggId != bggId) throw new InvalidOperationException("BGG вернул данные другой игры.");
        return (snapshot, BggGameMapper.ToOwnership(details, selectedExpansionIds));
    }

    public async Task<GatheringGameSnapshot> FromCampCatalogAsync(
        string communityKey,
        long bggId,
        IReadOnlyCollection<long> selectedExpansionIds,
        CancellationToken cancellationToken, long telegramUserId = 0)
    {
        var effective = (await new GameCatalogService(dbContext, campCatalog!)
                .LoadAsync(communityKey, Common.Options.BotMode.Camp, telegramUserId, cancellationToken))
            .SingleOrDefault(x => x.Game.BggId == bggId && x.IsBaseGame)
            ?? throw new KeyNotFoundException("Игра не найдена в каталоге этого кэмпа.");
        return await FromSavedGameAsync(effective.Game, selectedExpansionIds, cancellationToken);
    }

    private async Task<GatheringGameSnapshot> FromSavedGameAsync(ClubCollectionGame savedGame,
        IReadOnlyCollection<long> selectedExpansionIds, CancellationToken cancellationToken)
    {
        try
        {
            var details = await new BggSelectionService(bggClient).LoadBaseSelectionAsync(savedGame.BggId,
                selectedExpansionIds.Except(savedGame.Expansions.Select(x => x.BggId)).ToArray(), cancellationToken);
            var game = BggGameMapper.ToCollectionGame(details);
            return GatheringGameSnapshot.FromClubGame(game with
            { Expansions = ClubCollectionExpansion.Merge(game.Expansions, savedGame.Expansions) }, selectedExpansionIds);
        }
        catch (KeyNotFoundException)
        {
            logger?.LogWarning("BGG returned no details for saved game {BggId}; using saved metadata.", savedGame.BggId);
        }
        catch (HttpRequestException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(exception,
                "BGG details unavailable for saved game {BggId}; using the saved gathering snapshot.",
                savedGame.BggId);
        }

        return GatheringGameSnapshot.FromClubGame(savedGame, selectedExpansionIds);
    }

    private static GatheringGameSnapshot FromCanonicalDetails(BggGameDetails details,
        IReadOnlyCollection<long> selectedExpansionIds, string source)
    {
        return GatheringGameSnapshot.FromClubGame(
            BggGameMapper.ToCollectionGame(details), selectedExpansionIds, source);
    }

}
