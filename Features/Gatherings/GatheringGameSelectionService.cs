using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringGameSelectionService(
    AppDbContext dbContext,
    IBoardGameGeekClient bggClient)
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
            .Select(value => new ClubCollectionExpansion(value.BggId, value.Name))
            .ToArray();
        EnsureKnownExpansions(expansions, selectedExpansionIds);
        var game = details.Game;
        return new GatheringGameSnapshot(
            GatheringGameSnapshot.CurrentVersion,
            game.BggId,
            game.Name,
            game.ThumbnailImageUrl,
            game.ImageUrl,
            game.MinPlayers,
            game.MaxPlayers,
            game.BestPlayers,
            expansions.Where(value => selectedExpansionIds.Contains(value.BggId)).ToArray(),
            "bgg",
            expansions);
    }

    public async Task<GatheringGameSnapshot> FromCampCatalogAsync(
        string communityKey,
        long bggId,
        IReadOnlyCollection<long> selectedExpansionIds,
        CancellationToken cancellationToken)
    {
        var camp = await dbContext.Camps.AsNoTracking()
            .Include(value => value.Contributions)
            .SingleOrDefaultAsync(value => value.BotChatKey == communityKey, cancellationToken)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        var baseGame = camp.ReadBaseCollection().Games.SingleOrDefault(value => value.BggId == bggId);
        if (baseGame is not null)
        {
            var contributedExpansions = ReadContributionExpansions(camp, bggId);
            var allExpansions = baseGame.Expansions.Concat(contributedExpansions)
                .DistinctBy(value => value.BggId)
                .ToArray();
            EnsureKnownExpansions(allExpansions, selectedExpansionIds);
            return GatheringGameSnapshot.FromClubGame(
                baseGame with { Expansions = allExpansions },
                selectedExpansionIds);
        }

        var contribution = camp.Contributions.FirstOrDefault(value =>
            value.BggId == bggId && value.ItemType == Data.Entities.CampContributionItemType.BaseGame)
            ?? throw new KeyNotFoundException("Игра не найдена в каталоге этого кэмпа.");
        var contributionSnapshot = contribution.ReadSnapshot();
        var expansions = ReadContributionExpansions(camp, bggId);
        EnsureKnownExpansions(expansions, selectedExpansionIds);
        return new GatheringGameSnapshot(
            GatheringGameSnapshot.CurrentVersion,
            bggId,
            contributionSnapshot.Name,
            contributionSnapshot.ThumbnailImageUrl,
            contributionSnapshot.ImageUrl,
            contributionSnapshot.MinPlayers,
            contributionSnapshot.MaxPlayers,
            contributionSnapshot.BestPlayers,
            expansions.Where(value => selectedExpansionIds.Contains(value.BggId)).ToArray(),
            "catalog",
            expansions);
    }

    private static ClubCollectionExpansion[] ReadContributionExpansions(Data.Entities.Camp camp, long parentBggId) =>
        camp.Contributions
            .Where(value => value.ItemType == Data.Entities.CampContributionItemType.Expansion
                && value.ParentBggId == parentBggId)
            .Select(value =>
            {
                return new ClubCollectionExpansion(value.BggId, value.ReadSnapshot().Name);
            })
            .DistinctBy(value => value.BggId)
            .ToArray();

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
