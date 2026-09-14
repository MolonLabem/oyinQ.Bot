using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class GameComplexityTests
{
    // Synthetic poll fixture: live XML availability is not assumed.
    private static XElement Thing(string? weight, string poll = "") => XElement.Parse(
        $"<item><statistics><ratings><averageweight value='{weight}' /></ratings></statistics>{poll}</item>");
    private static string Poll(string results) => $"<poll name='boardgameweight'><results>{results}</results></poll>";
    private static string Vote(string level, string count) => $"<result value='{level}' numvotes='{count}' />";

    [Theory]
    [InlineData("Light", GameComplexity.Light, "Очень лёгкая", "complexity-very-easy")]
    [InlineData("Medium Light", GameComplexity.MediumLight, "Лёгкая", "complexity-easy")]
    [InlineData("Medium", GameComplexity.Medium, "Средняя", "complexity-medium")]
    [InlineData("Medium Heavy", GameComplexity.MediumHeavy, "Сложная", "complexity-hard")]
    [InlineData("Heavy", GameComplexity.Heavy, "Очень сложная", "complexity-very-hard")]
    public void PollWinnerIsAuthoritative(string answer, GameComplexity expected, string label, string css)
    {
        var (weight, level) = BggComplexityParser.Parse(Thing("2.1234", Poll(Vote(answer, "100") + Vote("3", "1"))));
        Assert.Equal(2.1234m, weight);
        Assert.Equal(expected, level);
        var info = GameComplexityPresentation.Present(weight, level)!;
        Assert.Equal(expected, info.Level);
        Assert.Equal(label, info.DisplayName);
        Assert.Equal(css, info.CssClass);
    }

    [Theory]
    [InlineData("1", GameComplexity.Light)]
    [InlineData("1.49", GameComplexity.Light)]
    [InlineData("1.5", GameComplexity.MediumLight)]
    [InlineData("2.49", GameComplexity.MediumLight)]
    [InlineData("2.5", GameComplexity.Medium)]
    [InlineData("3.49", GameComplexity.Medium)]
    [InlineData("3.5", GameComplexity.MediumHeavy)]
    [InlineData("4.49", GameComplexity.MediumHeavy)]
    [InlineData("4.5", GameComplexity.Heavy)]
    [InlineData("5", GameComplexity.Heavy)]
    public void MissingEmptyOrMalformedPollUsesNearestBggScore(string weight, GameComplexity expected)
    {
        foreach (var poll in new[] { "", Poll(""), Poll(Vote("Heavy", "0")), Poll(Vote("Heavy", "bad")),
            Poll(Vote("Heavy", "-1")), Poll(Vote("Unknown", "100")), Poll(Vote("Heavy", "999999999999999999999")) })
            Assert.Equal(expected, BggComplexityParser.Parse(Thing(weight, poll)).Level);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("bad")] [InlineData("0")]
    [InlineData("-1")] [InlineData("0.99")] [InlineData("5.01")] [InlineData("NaN")] [InlineData("2,5")]
    public void InvalidAverageIsUnknown(string? weight)
    {
        var parsed = BggComplexityParser.Parse(Thing(weight));
        Assert.Null(parsed.Weight);
        Assert.Null(parsed.Level);
        Assert.Null(GameComplexityPresentation.Present(parsed.Weight, parsed.Level));
        Assert.Equal(GameComplexity.Heavy, BggComplexityParser.Parse(Thing(weight, Poll(Vote("5", "3")))).Level);
    }

    [Fact]
    public void CompletelyMissingStatsRemainUnknown()
    {
        Assert.Equal((null, (GameComplexity?)null), BggComplexityParser.Parse(XElement.Parse("<item />")));
    }

    [Theory]
    [InlineData("3.8", GameComplexity.MediumHeavy)]
    [InlineData("2.2", GameComplexity.MediumLight)]
    [InlineData("3", GameComplexity.MediumLight)]
    [InlineData(null, GameComplexity.MediumLight)]
    public void TiesUseAverageThenLighterRegardlessOfXmlOrder(string? weight, GameComplexity expected)
    {
        var a = Vote("Medium Heavy", "10"); var b = Vote("Medium Light", "10");
        Assert.Equal(expected, BggComplexityParser.Parse(Thing(weight, Poll(a + b))).Level);
        Assert.Equal(expected, BggComplexityParser.Parse(Thing(weight, Poll(b + a))).Level);
    }

    private static ExternalGame Game(decimal weight = 2.1234m) => new(42, "Игра", 1, 4, null, null,
        ComplexityWeight: weight, Complexity: GameComplexity.MediumHeavy);

    [Fact]
    public void ImportCatalogWishAndGatheringSnapshotsPreservePollCategoryAndPrecision()
    {
        var selection = BggGameMapper.ToImportSelection(Game(), CollectionItemType.BaseGame, null, []);
        var snapshot = CollectionItemSnapshotSerializer.Deserialize(CollectionItemSnapshotSerializer.Serialize(selection.ToSnapshot()));
        var club = snapshot.ToCollectionGame(42);
        var document = ClubCollectionSerializer.Deserialize(ClubCollectionSerializer.Serialize(new(2, [club])));
        var gathered = GatheringGameSnapshot.FromClubGame(document.Games.Single(), []);
        var saved = GatheringGameSnapshotSerializer.Serialize(gathered);
        var restored = GatheringGameSnapshotSerializer.Deserialize(saved);
        Assert.Equal(2.1234m, restored.ComplexityWeight);
        Assert.Equal(GameComplexity.MediumHeavy, restored.Complexity);
        Assert.DoesNotContain("cssClass", saved);
        Assert.DoesNotContain("displayName", saved);
        var edited = restored.WithExpansions([]);
        Assert.Equal(restored.Complexity, edited.Complexity);
        Assert.Equal(restored.ComplexityWeight, edited.ComplexityWeight);
        var refreshed = BggGameMapper.ToCollectionSelection(new(Game(4.5678m), []), [], club);
        Assert.Equal(4.5678m, refreshed.ComplexityWeight);
        Assert.Equal(GameComplexity.MediumHeavy, refreshed.Complexity);
        var merged = ClubBggImportService.Merge(document, [selection with { ComplexityWeight = 3.8765m }]);
        Assert.Equal(3.8765m, merged.Document.Games.Single().ComplexityWeight);
    }

    [Fact]
    public void ExpansionEnrichmentRefreshesComplexityAndCampProjectionRetainsIt()
    {
        var snapshot = BggGameMapper.ToCollectionSnapshot(Game(4.5678m), [1]);
        var expansion = BggGameMapper.ToCollectionExpansion(new(42, "Expansion", Snapshot: snapshot));
        var merged = BggGameMapper.MergeEnrichedExpansions(
            [expansion with { ComplexityWeight = 1m, Complexity = GameComplexity.Light }], [expansion]);
        Assert.Equal(4.5678m, Assert.Single(merged).ComplexityWeight);
        Assert.Equal(GameComplexity.MediumHeavy, Assert.Single(merged).Complexity);
        var baseGame = BggGameMapper.ToCollectionGame(Game() with { BggId = 1 }, merged);
        var camp = oyinQ.Bot.Features.Catalog.EffectiveCampCatalogService.Build(new(2, [baseGame]), []);
        var restored = GatheringGameSnapshotSerializer.Deserialize(GatheringGameSnapshotSerializer.Serialize(
            GatheringGameSnapshot.FromClubGame(Assert.Single(camp).Game, [42])));
        Assert.Equal(4.5678m, Assert.Single(restored.SelectedExpansions).ComplexityWeight);
        Assert.Equal(GameComplexity.MediumHeavy, Assert.Single(restored.SelectedExpansions).Complexity);
    }

    [Fact]
    public void OldSnapshotsAndHttpProjectionHaveSeparateContracts()
    {
        var old = CollectionItemSnapshotSerializer.Deserialize("""{"version":1,"name":"Old"}""");
        Assert.Null(GameComplexityPresentation.Present(old));
        var snapshot = BggGameMapper.ToCollectionSnapshot(Game());
        var persisted = CollectionItemSnapshotSerializer.Serialize(snapshot);
        Assert.DoesNotContain("complexityInfo", persisted);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ComplexityJsonPresentation.Add } }
        };
        options.Converters.Add(new JsonStringEnumConverter());
        using var http = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, options));
        Assert.Equal("Сложная", http.RootElement.GetProperty("complexityInfo").GetProperty("displayName").GetString());
        Assert.Equal("MediumHeavy", http.RootElement.GetProperty("complexityInfo").GetProperty("level").GetString());
        Assert.Equal(2.1234m, http.RootElement.GetProperty("complexityInfo").GetProperty("weight").GetDecimal());
    }

    [Fact]
    public async Task ReimportUpdatesOwnedMetadataWithoutChangingManualOwnership()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Participants.Add(new Participant { Id = 1, TelegramUserId = 42, DisplayName = "Игрок" });
        await db.SaveChangesAsync();
        var service = new ParticipantCollectionService(db);
        var item = BggGameMapper.ToDraftItem(new(Game(), false, []));
        await service.UpsertAsync(1, [item], CollectionItemSource.Manual, DateTimeOffset.UtcNow, default);
        await service.UpsertAsync(1, [item with { Snapshot = item.Snapshot with { ComplexityWeight = 4.1234m } }],
            CollectionItemSource.BggImport, DateTimeOffset.UtcNow, default);
        db.ChangeTracker.Clear();
        var owned = await db.ParticipantCollectionItems.SingleAsync();
        Assert.Equal(CollectionItemSource.Manual, owned.Source);
        Assert.Equal(4.1234m, owned.ReadSnapshot().ComplexityWeight);
        Assert.Equal(GameComplexity.MediumHeavy, owned.ReadSnapshot().Complexity);
    }
}
