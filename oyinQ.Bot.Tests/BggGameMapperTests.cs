using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggGameMapperTests
{
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
