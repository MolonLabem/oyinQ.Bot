using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Tests;

public sealed class ClubBggImportTests
{
    [Fact]
    public void Merge_IsAdditive_AndPreservesExistingExpansionSelections()
    {
        var current = new ClubCollectionDocument(2,
        [
            new ClubCollectionGame(10, "Old title", null, null, 1, 4, null,
                [new ClubCollectionExpansion(100, "Kept")], OriginalName: "Original title"),
            new ClubCollectionGame(20, "Club only", null, null, 2, 4, null, [])
        ]);
        CampImportSelectionItem[] imported =
        [
            new(10, CollectionItemType.BaseGame, null, "Fresh title", true,
                Description: "Fresh metadata", Type: GameType.Strategy, OriginalName: "Fresh original"),
            new(30, CollectionItemType.BaseGame, null, "New game", true),
            new(101, CollectionItemType.Expansion, 10, "New expansion", true,
                ParentBggIds: [10]),
            new(102, CollectionItemType.Expansion, 30, "Shared expansion", true,
                ParentBggIds: [10, 30]),
            new(103, CollectionItemType.Expansion, 999, "Orphan", true,
                ParentBggIds: [999])
        ];

        var result = ClubBggImportService.Merge(current, imported);

        Assert.Equal(1, result.AddedGames);
        Assert.Equal(3, result.AddedExpansions);
        Assert.Equal(1, result.OrphanExpansions);
        Assert.Contains(result.Document.Games, game => game.BggId == 20);
        var refreshed = Assert.Single(result.Document.Games, game => game.BggId == 10);
        Assert.Equal("Fresh title", refreshed.Name);
        Assert.Equal("Fresh original", refreshed.OriginalName);
        Assert.Equal("Fresh metadata", refreshed.Description);
        Assert.Equal(GameType.Strategy, refreshed.Type);
        Assert.Equal(3, result.Document.Games.Count);
        Assert.Equal([100L, 101L, 102L], refreshed.Expansions.Select(x => x.BggId).Order());
        Assert.Equal([102L], Assert.Single(result.Document.Games, game => game.BggId == 30)
            .Expansions.Select(x => x.BggId));

        var repeated = ClubBggImportService.Merge(result.Document, imported);
        var repeatedGame = Assert.Single(repeated.Document.Games, game => game.BggId == 10);
        Assert.Equal(GameType.Strategy, repeatedGame.Type);
        Assert.Equal([100L, 101L, 102L], repeatedGame.Expansions.Select(x => x.BggId).Order());
        Assert.Equal(0, repeated.AddedGames);
        Assert.Equal(0, repeated.AddedExpansions);
    }

    [Fact]
    public void Merge_NormalizesInvalidProviderMetadata()
    {
        var imported = new CampImportSelectionItem(10, CollectionItemType.BaseGame, null,
            "Omerta", true, Description: new string('x', 20_001), YearPublished: 0,
            MinPlayTimeMinutes: 60, MaxPlayTimeMinutes: 30, MinAge: 200);

        var game = Assert.Single(ClubBggImportService.Merge(ClubCollectionDocument.Empty, [imported])
            .Document.Games);

        Assert.Equal(20_000, game.Description?.Length);
        Assert.Null(game.YearPublished);
        Assert.Equal(30, game.MinPlayTimeMinutes);
        Assert.Equal(60, game.MaxPlayTimeMinutes);
        Assert.Null(game.MinAge);
        ClubCollectionSerializer.Validate(new ClubCollectionDocument(2, [game]));
    }

    [Fact]
    public void Merge_PreservesAndDeduplicatesOfficialExpansionRelationships()
    {
        CampImportSelectionItem[] imported =
        [
            new(10, CollectionItemType.BaseGame, null, "Base", true),
            new(20, CollectionItemType.BaseGame, null, "Other base", true),
            new(100, CollectionItemType.Expansion, 10, "Shared expansion", true,
                ParentBggIds: [10, 20]),
            new(100, CollectionItemType.Expansion, 10, "Duplicate provider row", true,
                ParentBggIds: [10, 20])
        ];

        var result = ClubBggImportService.Merge(ClubCollectionDocument.Empty, imported);

        Assert.Equal([100L], Assert.Single(result.Document.Games, game => game.BggId == 10)
            .Expansions.Select(expansion => expansion.BggId));
        Assert.Equal([100L], Assert.Single(result.Document.Games, game => game.BggId == 20)
            .Expansions.Select(expansion => expansion.BggId));
        Assert.Equal(2, result.AddedExpansions);
        Assert.Equal(0, result.OrphanExpansions);
    }
}
