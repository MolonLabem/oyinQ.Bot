using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public static class BggGameMapper
{
    public static BggExpansion ToBggExpansion(BggCollectionItem item)
    {
        var snapshot = ToCollectionSnapshot(item.Game, item.ParentBggIds);
        return new(item.Game.BggId!.Value, snapshot.Name, snapshot.OriginalName,
            snapshot.MinPlayers, snapshot.MaxPlayers, snapshot);
    }

    public static ClubCollectionExpansion ToCollectionExpansion(BggExpansion expansion) =>
        new(expansion.BggId, expansion.Name, expansion.OriginalName, expansion.MinPlayers, expansion.MaxPlayers);

    public static ClubCollectionGame ToCollectionGame(BggGameDetails details) =>
        ToCollectionGame(details.Game, details.Expansions.Select(ToCollectionExpansion).DistinctBy(x => x.BggId).ToArray());

    public static ClubCollectionGame ToCollectionSelection(BggGameDetails details,
        IReadOnlyCollection<long> selectedIds, ClubCollectionGame? existing = null)
    {
        var game = ToCollectionGame(details);
        var selected = GatheringExpansionSelection.Select(game.Expansions, selectedIds);
        if (existing is null) return game with { Expansions = selected };
        if (existing.BggId != game.BggId) throw new InvalidOperationException("BGG вернул данные другой игры.");
        return existing.WithMetadataFallback(ToCollectionSnapshot(details.Game)) with
        {
            Expansions = ClubCollectionExpansion.Merge(existing.Expansions, selected)
        };
    }

    public static CampBggImportDraftItem ToDraftItem(BggCollectionItem item) => new(item.Game.BggId!.Value,
        item.IsExpansion ? CollectionItemType.Expansion : CollectionItemType.BaseGame,
        item.ParentBggIds.FirstOrDefault() is > 0 and var parent ? parent : null,
        ToCollectionSnapshot(item.Game, item.ParentBggIds), ParentBggIds: item.ParentBggIds);

    public static IReadOnlyList<CampBggImportDraftItem> ToOwnership(BggGameDetails details, IReadOnlyCollection<long> selectedIds)
    {
        var game = ToCollectionGame(details);
        var selected = GatheringExpansionSelection.Select(game.Expansions, selectedIds);
        List<CampBggImportDraftItem> items = [new(game.BggId, CollectionItemType.BaseGame, null, ToCollectionSnapshot(details.Game))];
        foreach (var expansion in selected)
        {
            var provider = details.Expansions.First(x => x.BggId == expansion.BggId);
            items.Add(ToExpansionOwnership(game.BggId, expansion, provider.Snapshot));
        }
        return items;
    }

    public static CampBggImportDraftItem ToExpansionOwnership(long baseId, ClubCollectionExpansion expansion,
        CollectionItemSnapshot? metadata = null)
    {
        var snapshot = metadata ?? new CollectionItemSnapshot(CollectionItemSnapshot.CurrentVersion,
            expansion.Name, null, null, expansion.MinPlayers, expansion.MaxPlayers, null,
            ParentBggIds: [baseId], OriginalName: expansion.OriginalName);
        var parents = (snapshot.ParentBggIds ?? []).Append(baseId).Distinct().ToArray();
        return new(expansion.BggId, CollectionItemType.Expansion, baseId,
            snapshot with { ParentBggIds = parents }, ParentBggIds: parents);
    }

    public static CampImportSelectionItem ToImportSelection(ExternalGame game, CollectionItemType type,
        long? parentId, IReadOnlyList<long>? parents)
    {
        var snapshot = ToCollectionSnapshot(game, parents);
        return new(game.BggId!.Value, type, parentId, snapshot.Name, true,
            snapshot.ThumbnailImageUrl, snapshot.ImageUrl, snapshot.MinPlayers, snapshot.MaxPlayers,
            snapshot.BestPlayers, snapshot.Types, snapshot.Categories, snapshot.Description,
            snapshot.YearPublished, snapshot.MinPlayTimeMinutes, snapshot.MaxPlayTimeMinutes,
            snapshot.MinAge, snapshot.Type, snapshot.Subdomains, snapshot.CategoryItems,
            snapshot.Mechanics, snapshot.ParentBggIds, snapshot.OriginalName);
    }

    public static ClubCollectionGame ToCollectionGame(ExternalGame game,
        IReadOnlyList<ClubCollectionExpansion>? expansions = null)
    {
        game = NormalizeMetadata(game);
        if (game.BggId is not > 0)
            throw new InvalidOperationException("Игра BGG должна иметь положительный ID.");

        return new ClubCollectionGame(game.BggId.Value, game.Name, game.ThumbnailImageUrl, game.ImageUrl,
            game.MinPlayers, game.MaxPlayers, game.BestPlayers, expansions ?? [], game.Types, game.Categories,
            game.Description, game.YearPublished, game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge,
            game.Type, game.Subdomains, game.CategoryItems, game.Mechanics, game.OriginalName);
    }

    public static CollectionItemSnapshot ToCollectionSnapshot(CampImportSelectionItem item) =>
        ToCollectionSnapshot(new ExternalGame(item.BggId, item.Name, item.MinPlayers, item.MaxPlayers,
            item.BestPlayers, BggGameUrl.FromId(item.BggId), item.ThumbnailImageUrl, item.ImageUrl,
            item.Types, item.Categories, item.Description, item.YearPublished, item.MinPlayTimeMinutes,
            item.MaxPlayTimeMinutes, item.MinAge, item.Subdomains, item.CategoryItems, item.Mechanics,
            item.Type, item.OriginalName), item.ParentBggIds);

    public static CollectionItemSnapshot ToCollectionSnapshot(ExternalGame game,
        IReadOnlyList<long>? parentBggIds = null)
    {
        game = NormalizeMetadata(game);
        var players = PlayerCountRange.Normalize(game.MinPlayers, game.MaxPlayers);
        return new(CollectionItemSnapshot.CurrentVersion, game.Name, game.ThumbnailImageUrl, game.ImageUrl,
            players.WasDefaulted ? null : players.Minimum, players.WasDefaulted ? null : players.Maximum,
            game.BestPlayers, game.Types, game.Categories, game.Description,
            game.YearPublished, game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge, game.Type,
            game.Subdomains, game.CategoryItems, game.Mechanics, parentBggIds, game.OriginalName);
    }

    private static ExternalGame NormalizeMetadata(ExternalGame game)
    {
        var minimum = game.MinPlayTimeMinutes is >= 0 ? game.MinPlayTimeMinutes : null;
        var maximum = game.MaxPlayTimeMinutes is >= 0 ? game.MaxPlayTimeMinutes : null;
        if (minimum > maximum) (minimum, maximum) = (maximum, minimum);
        var description = game.Description;
        if (description is { Length: > 20_000 })
        {
            var length = char.IsHighSurrogate(description[19_999]) ? 19_999 : 20_000;
            description = description[..length];
        }
        return game with
        {
            Description = description,
            YearPublished = game.YearPublished is >= 1000 and <= 3000 ? game.YearPublished : null,
            MinPlayTimeMinutes = minimum,
            MaxPlayTimeMinutes = maximum,
            MinAge = game.MinAge is >= 0 and <= 100 ? game.MinAge : null
        };
    }
}
