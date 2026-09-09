using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggGameMapperTests
{
    [Theory]
    [InlineData(90, 30, 200, 30, 90, null)]
    [InlineData(-1, 60, -1, null, 60, null)]
    [InlineData(60, -1, 12, 60, null, 12)]
    [InlineData(null, null, null, null, null, null)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    public void InvalidOptionalMetadata_IsNormalizedForBothCollections(int? minimum, int? maximum,
        int? age, int? expectedMinimum, int? expectedMaximum, int? expectedAge)
    {
        var provider = new ExternalGame(42, "Игра", 1, 4, null, null,
            Description: new string('x', 19_999) + "🎲tail", YearPublished: 0,
            MinPlayTimeMinutes: minimum, MaxPlayTimeMinutes: maximum, MinAge: age);
        var snapshot = BggGameMapper.ToCollectionSnapshot(provider);
        var game = BggGameMapper.ToCollectionGame(provider);
        Assert.Equal(expectedMinimum, snapshot.MinPlayTimeMinutes);
        Assert.Equal(expectedMaximum, snapshot.MaxPlayTimeMinutes);
        Assert.Equal(expectedAge, snapshot.MinAge);
        Assert.Equal(new string('x', 19_999), snapshot.Description);
        Assert.Null(snapshot.YearPublished);
        Assert.Equal(snapshot.MinPlayTimeMinutes, game.MinPlayTimeMinutes);
        Assert.Equal(snapshot.MaxPlayTimeMinutes, game.MaxPlayTimeMinutes);
        Assert.Equal(snapshot.MinAge, game.MinAge);
        Assert.Equal(snapshot.Description, game.Description);
        Assert.Equal(snapshot.YearPublished, game.YearPublished);
        _ = CollectionItemSnapshotSerializer.Serialize(snapshot);
        _ = ClubCollectionSerializer.Serialize(new(2, [game]));
    }

    [Fact]
    public void ProviderGame_UsesOneMetadataProjectionForCollectionAndGathering()
    {
        var provider = new ExternalGame(167791, "Terraforming Mars", 1, 5, "3",
            BggGameUrl.FromId(167791), Type: GameType.Strategy, OriginalName: "Terraforming Mars");
        var expansion = new ClubCollectionExpansion(296108, "Turmoil");

        var collection = BggGameMapper.ToCollectionGame(provider, [expansion]);
        var contribution = BggGameMapper.ToCollectionSnapshot(provider);
        var gathering = GatheringGameSnapshot.FromClubGame(collection, [expansion.BggId], "bgg");

        Assert.Equal(provider.BggId, collection.BggId);
        Assert.Equal(collection.Name, gathering.Name);
        Assert.Equal(collection.OriginalName, gathering.OriginalName);
        Assert.Equal(collection.Name, contribution.Name);
        Assert.Equal(collection.OriginalName, contribution.OriginalName);
        Assert.Equal(collection.Type, contribution.Type);
        Assert.Equal(GameType.Strategy, gathering.Type);
        Assert.Equal("bgg", gathering.Source);
        Assert.Equal(expansion.BggId, Assert.Single(gathering.SelectedExpansions).BggId);
    }

    [Fact]
    public void ProviderGame_RequiresCanonicalBggIdentity() =>
        Assert.Throws<InvalidOperationException>(() => BggGameMapper.ToCollectionGame(
            new ExternalGame(null, "No identity", 1, 4, null, null)));
    [Theory]
    [InlineData(0, 4)]
    [InlineData(5, 2)]
    public void OwnershipSnapshot_DoesNotStoreInvalidProviderPlayerRange(int min, int max)
    {
        var snapshot = BggGameMapper.ToCollectionSnapshot(new ExternalGame(42, "Игра", min, max, null, null));
        Assert.Null(snapshot.MinPlayers); Assert.Null(snapshot.MaxPlayers);
        Assert.NotEmpty(CollectionItemSnapshotSerializer.Serialize(snapshot));
    }
}
