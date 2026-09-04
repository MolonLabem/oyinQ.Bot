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
    public async Task<GatheringGameSnapshot> FromClubCollectionAsync(
        string communityKey, long bggId, IReadOnlyCollection<long> selectedExpansionIds,
        CancellationToken cancellationToken, long telegramUserId = 0)
    {
        var catalog = new GameCatalogService(dbContext, campCatalog!);
        var game = (await catalog.LoadClubAsync(communityKey, telegramUserId, cancellationToken))
            .SingleOrDefault(x => x.Game.BggId == bggId)?.Game
            ?? throw new KeyNotFoundException("Игра не найдена в доступной вам коллекции.");
        return await FromSavedGameAsync(game, selectedExpansionIds, cancellationToken);
    }

    public async Task<GatheringGameSnapshot> FromArbitraryBggAsync(long bggId,
        IReadOnlyCollection<long> selectedExpansionIds, CancellationToken cancellationToken) =>
        (await ExternalSelectionAsync(bggId, selectedExpansionIds, cancellationToken)).Snapshot;

    public async Task<(GatheringGameSnapshot Snapshot, IReadOnlyList<CampBggImportDraftItem> Ownership)> ExternalSelectionAsync(
        long bggId, IReadOnlyCollection<long> selectedExpansionIds, CancellationToken cancellationToken)
    {
        var details = await bggClient.GetGameDetailsAsync(bggId, cancellationToken)
            ?? throw new KeyNotFoundException("Игра не найдена в BGG.");
        var snapshot = FromCanonicalDetails(details, selectedExpansionIds, "bgg");
        if (snapshot.BggId != bggId) throw new InvalidOperationException("BGG вернул данные другой игры.");
        List<CampBggImportDraftItem> items = [new(bggId, CollectionItemType.BaseGame, null,
            BggGameMapper.ToCollectionSnapshot(details.Game))];
        // Details already contain provider-validated expansion identities and titles. Do not invent rich metadata.
        items.AddRange(details.Expansions.Where(x => selectedExpansionIds.Contains(x.BggId)).Select(x =>
            new CampBggImportDraftItem(x.BggId, CollectionItemType.Expansion, bggId,
                new(CollectionItemSnapshot.CurrentVersion, x.Name, null, null, null, null, null,
                    ParentBggIds: [bggId], OriginalName: x.OriginalName))));
        return (snapshot, items);
    }

    public async Task<GatheringGameSnapshot> FromCampCatalogAsync(
        string communityKey,
        long bggId,
        IReadOnlyCollection<long> selectedExpansionIds,
        CancellationToken cancellationToken)
    {
        var effective = (await (campCatalog ?? throw new InvalidOperationException("Каталог кэмпа недоступен."))
                .LoadAsync(communityKey, null, cancellationToken))
            .SingleOrDefault(x => x.Game.BggId == bggId)
            ?? throw new KeyNotFoundException("Игра не найдена в каталоге этого кэмпа.");
        return await FromSavedGameAsync(effective.Game, selectedExpansionIds, cancellationToken);
    }

    private async Task<GatheringGameSnapshot> FromSavedGameAsync(ClubCollectionGame savedGame,
        IReadOnlyCollection<long> selectedExpansionIds, CancellationToken cancellationToken)
    {
        try
        {
            var details = await bggClient.GetGameDetailsAsync(savedGame.BggId, cancellationToken);
            if (details is not null)
            {
                return FromCanonicalDetails(details, selectedExpansionIds, "catalog");
            }

            logger?.LogWarning(
                "BGG returned no details for saved game {BggId}; using the saved gathering snapshot.",
                savedGame.BggId);
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
        var expansions = details.Expansions
            .DistinctBy(value => value.BggId)
            .Select(value => new ClubCollectionExpansion(value.BggId, value.Name, value.OriginalName))
            .ToArray();
        return GatheringGameSnapshot.FromClubGame(
            BggGameMapper.ToCollectionGame(details.Game, expansions), selectedExpansionIds, source);
    }

}
