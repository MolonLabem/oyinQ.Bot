using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class CatalogMetadataTests
{
    [Fact]
    public void VersionOneDocument_IsReadAsCurrentVersion()
    {
        var document = ClubCollectionSerializer.Deserialize("""
            {"version":1,"games":[{"bggId":1,"name":"Legacy","expansions":[]}]}
            """);

        Assert.Equal(ClubCollectionDocument.CurrentVersion, document.Version);
        Assert.Equal(GameType.Other, Assert.Single(document.Games).Type);
    }

    [Fact]
    public void VersionTwoMetadata_RoundTrips()
    {
        var game = Game() with { Description = "Описание", YearPublished = 2020, MinAge = 12,
            Type = GameType.Strategy, CategoryItems = [new(1021, "Economic")], Mechanics = [new(2040, "Hand Management")] };
        var restored = ClubCollectionSerializer.Deserialize(ClubCollectionSerializer.Serialize(new(2, [game])));

        var restoredGame = Assert.Single(restored.Games);
        Assert.Equal(game.BggId, restoredGame.BggId);
        Assert.Equal(game.Description, restoredGame.Description);
        Assert.Equal(game.YearPublished, restoredGame.YearPublished);
        Assert.Equal(game.Type, restoredGame.Type);
        Assert.Equal(game.CategoryItems, restoredGame.CategoryItems);
        Assert.Equal(game.Mechanics, restoredGame.Mechanics);
    }

    [Fact]
    public void Taxonomy_UsesRussianTranslationAndEnglishFallback()
    {
        Assert.Equal("Экономическая", BggTaxonomyCatalog.LocalizeCategory(new(1021, "Economic")));
        Assert.Equal("Пираты", BggTaxonomyCatalog.LocalizeCategory(new(1090, "Pirates")));
        Assert.Equal("Очки действий", BggTaxonomyCatalog.LocalizeMechanic(new(2001, "Action Points")));
        Assert.Equal("Скрытые роли", BggTaxonomyCatalog.LocalizeMechanic(new(2891, "Hidden Roles")));
        Assert.Equal("Unknown category", BggTaxonomyCatalog.LocalizeCategory(new(999999, "Unknown category")));
        Assert.Equal(GameType.Strategy, BggTaxonomyCatalog.MapGameType([new(5497, "Strategy Games")]));
        Assert.Equal([GameType.Family, GameType.Strategy], BggTaxonomyCatalog.MapGameTypes(
            [new(5497, "Strategy Games"), new(5499, "Family Games")]));
        Assert.Equal(GameType.Thematic, BggTaxonomyCatalog.ResolveType(
            GameType.Other,
            [new(5496, "Thematic Games")],
            ["Other"]));
        Assert.Equal(GameType.War, BggTaxonomyCatalog.ResolveType(
            GameType.Other,
            [],
            [],
            [new(1019, "Wargame")],
            ["Wargame"]));
        Assert.Equal(GameType.Family,
            BggTaxonomyCatalog.ResolveType(GameType.Other, null, ["Family Games"]));
        Assert.Equal(GameType.War,
            BggTaxonomyCatalog.ResolveType(GameType.Other, null, ["Wargames"]));
    }

    [Fact]
    public void SharedFilter_CombinesPlayerTypeAndCategory()
    {
        var game = Game() with { MinPlayers = 2, MaxPlayers = 4, Type = GameType.Strategy,
            Subdomains = [new(5499, "Family Games"), new(5497, "Strategy Games")],
            CategoryItems = [new(1021, "Economic")] };
        Assert.True(GameCatalogService.Matches(game, new CatalogQuery(null, 3, [GameType.Strategy], [1021], null)));
        Assert.True(GameCatalogService.Matches(game, new CatalogQuery(null, 3, [GameType.Family], [1021], null)));
        Assert.False(GameCatalogService.Matches(game, new CatalogQuery(null, 5, [GameType.Strategy], [1021], null)));
        Assert.False(GameCatalogService.Matches(game, new CatalogQuery(null, 3, [GameType.Party], [1021], null)));
        Assert.False(GameCatalogService.Matches(game, new CatalogQuery(null, 3, [GameType.Strategy], [999], null)));
    }

    [Fact]
    public void CampCity_IsTrimmedAndValidated()
    {
        Assert.Equal("Астана", CampRules.NormalizeCity("  Астана  "));
        Assert.Throws<ArgumentException>(() => CampRules.NormalizeCity(" "));
        Assert.Throws<ArgumentException>(() => CampRules.NormalizeCity(new string('x', 101)));
    }

    [Fact]
    public void MetadataEnrichment_PreservesGameAndSelectedExpansionMembership()
    {
        var existing = Game() with { Expansions = [new(10, "Old expansion"), new(11, "Orphan selection")] };
        var details = new BggGameDetails(new ExternalGame(1, "Updated", 1, 5, "3", null,
            Description: "New metadata", Type: GameType.Strategy), [new(10, "Updated expansion"), new(12, "Not owned")]);

        var enriched = ClubMetadataRefreshService.EnrichPreservingMembership(existing, details);

        Assert.Equal(1, enriched.BggId);
        Assert.Equal([10L, 11L], enriched.Expansions.Select(x => x.BggId));
        Assert.Equal("Updated expansion", enriched.Expansions[0].Name);
        Assert.Equal("Orphan selection", enriched.Expansions[1].Name);
    }

    [Fact]
    public void ImportSkipClassification_DoesNotTreatAnotherOwnerAsDuplicate()
    {
        var draft = new CampBggImportDraft(2, "owner", [
            new(1, CampContributionItemType.BaseGame, null, new CampContributionSnapshot(2, "Shared", null, null, 2, 4, null)),
            new(2, CampContributionItemType.BaseGame, null, new CampContributionSnapshot(2, "Base", null, null, 2, 4, null)),
            new(3, CampContributionItemType.BaseGame, null, new CampContributionSnapshot(2, "Manual", null, null, 2, 4, null))]);

        var classified = CampBggImportService.ClassifySkips(draft, new HashSet<long> { 2 },
            new HashSet<(long, CampContributionItemType)> { (3, CampContributionItemType.BaseGame) });

        Assert.Null(classified.Items[0].SkipReason); // another participant may already own it; that is not a skip
        Assert.Equal(CampImportSkipReason.AlreadyInBaseCollection, classified.Items[1].SkipReason);
        Assert.True(classified.Items[1].IsOverridable);
        Assert.Equal(CampImportSkipReason.AlreadyAddedManually, classified.Items[2].SkipReason);
    }

    private static ClubCollectionGame Game() => new(1, "Brass", null, null, 2, 4, "3–4", []);
}
