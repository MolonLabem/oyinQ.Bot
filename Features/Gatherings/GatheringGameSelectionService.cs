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
        var known = (snapshot.KnownExpansions ?? snapshot.SelectedExpansions).Select(item =>
            metadata.FirstOrDefault(value => value.BggId == item.BggId) is { } extra
                ? item.WithMetadataFallback(extra) : item).ToArray();
        return (snapshot with { KnownExpansions = known })
            .WithExpansions(snapshot.SelectedExpansions.Select(item => item.BggId).ToArray());
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
            var details = await new BggSelectionService(bggClient).LoadBaseSelectionAsync(savedGame.BggId, selectedExpansionIds, cancellationToken);
            return FromCanonicalDetails(details, selectedExpansionIds, "catalog");
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
