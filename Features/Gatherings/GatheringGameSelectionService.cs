using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringGameSelectionService(
    AppDbContext dbContext,
    IBoardGameGeekClient bggClient,
    EffectiveCampCatalogService? campCatalog = null)
{
    public async Task<GatheringGameSnapshot> FromClubCollectionAsync(
        string communityKey,
        long bggId,
        IReadOnlyCollection<long> selectedExpansionIds,
        CancellationToken cancellationToken)
    {
        var json = await dbContext.Clubs.AsNoTracking()
            .Where(value => value.BotChatKey == communityKey)
            .Select(value => value.CollectionJson)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Коллекция клуба не найдена.");
        var game = ClubCollectionSerializer.Deserialize(json).Games.SingleOrDefault(value => value.BggId == bggId)
            ?? throw new KeyNotFoundException("Игра не найдена в коллекции этого клуба.");
        EnsureKnownExpansions(game.Expansions, selectedExpansionIds);
        return GatheringGameSnapshot.FromClubGame(game, selectedExpansionIds);
    }

    public async Task<GatheringGameSnapshot> FromArbitraryBggAsync(
        long bggId,
        IReadOnlyCollection<long> selectedExpansionIds,
        CancellationToken cancellationToken)
    {
        var details = await bggClient.GetGameDetailsAsync(bggId, cancellationToken)
            ?? throw new KeyNotFoundException("Игра не найдена в BGG.");
        var expansions = details.Expansions
            .Select(value => new ClubCollectionExpansion(value.BggId, value.Name, value.OriginalName))
            .ToArray();
        EnsureKnownExpansions(expansions, selectedExpansionIds);
        var game = details.Game;
        var players = PlayerCountRange.Normalize(game.MinPlayers, game.MaxPlayers);
        return new GatheringGameSnapshot(
            GatheringGameSnapshot.CurrentVersion,
            game.BggId,
            game.Name,
            game.ThumbnailImageUrl,
            game.ImageUrl,
            players.Minimum,
            players.Maximum,
            game.BestPlayers,
            expansions.Where(value => selectedExpansionIds.Contains(value.BggId)).ToArray(),
            "bgg",
            expansions,
            game.Description,
            game.YearPublished,
            game.MinPlayTimeMinutes,
            game.MaxPlayTimeMinutes,
            game.MinAge,
            BggTaxonomyCatalog.ResolveType(game.Type, game.Subdomains, game.Types,
                game.CategoryItems, game.Categories),
            game.CategoryItems,
            game.Mechanics,
            players.WasDefaulted,
            game.OriginalName);
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
        EnsureKnownExpansions(effective.Game.Expansions, selectedExpansionIds);
        return GatheringGameSnapshot.FromClubGame(effective.Game, selectedExpansionIds);
    }

    private static void EnsureKnownExpansions(
        IReadOnlyCollection<ClubCollectionExpansion> available,
        IReadOnlyCollection<long> selectedIds)
    {
        var knownIds = available.Select(value => value.BggId).ToHashSet();
        if (selectedIds.Any(value => !knownIds.Contains(value)))
        {
            throw new InvalidOperationException("Выбрано дополнение, которое не относится к этой игре.");
        }
    }
}
