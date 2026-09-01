using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class ClubCollectionRefreshGeneratorTests
{
    [Fact]
    public void ExtractBggIds_ReadsNestedAddons_DeduplicatesAndSkipsInvalidValues()
    {
        var result = ClubCollectionRefreshGenerator.ExtractBggIds("""
            [
              { "game": { "bggId": 10, "addons": [
                { "game": { "bggId": "20" } },
                { "game": { "bggId": 0 } }
              ] } },
              { "game": { "bggId": 10 } },
              { "game": { "title": "Missing" } }
            ]
            """);

        Assert.Equal([10L, 20L], result.BggIds.Order());
        Assert.Equal(2, result.InvalidEntries);
    }

    [Fact]
    public void BuildDocument_UsesBggClassificationAndOfficialParentLinks()
    {
        var baseGame = Game(10, "Base");
        var expansion = Game(20, "Expansion");
        var orphan = Game(30, "Orphan");

        var document = ClubCollectionRefreshGenerator.BuildDocument([
            new BggCollectionItem(baseGame, false, []),
            new BggCollectionItem(expansion, true, [10]),
            new BggCollectionItem(orphan, true, [999])
        ], out var orphanExpansions);

        var game = Assert.Single(document.Games);
        Assert.Equal(10, game.BggId);
        Assert.Equal(20, Assert.Single(game.Expansions).BggId);
        Assert.Equal(1, orphanExpansions);
    }

    private static ExternalGame Game(long id, string name) =>
        new(id, name, 2, 4, "3", null);
}
