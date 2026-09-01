using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class PlayerCountRangeTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(0, 6)]
    [InlineData(2, 0)]
    [InlineData(5, 2)]
    public void Normalize_DefaultsIncompleteOrInvalidRanges(int? minimum, int? maximum)
    {
        var result = PlayerCountRange.Normalize(minimum, maximum);

        Assert.Equal((1, 12, true), (result.Minimum, result.Maximum, result.WasDefaulted));
    }

    [Fact]
    public void Normalize_PreservesCompleteValidRange()
    {
        var result = PlayerCountRange.Normalize(2, 5);

        Assert.Equal((2, 5, false), (result.Minimum, result.Maximum, result.WasDefaulted));
    }

    [Fact]
    public void SnapshotBoundary_PersistsTheSameFallbackUsedByTheUi()
    {
        var game = new ClubCollectionGame(10, "Game", null, null, 0, 0, null, []);

        var snapshot = GatheringGameSnapshot.FromClubGame(game, []);
        var restored = GatheringGameSnapshotSerializer.Deserialize(
            GatheringGameSnapshotSerializer.Serialize(snapshot));

        Assert.Equal(1, restored.MinPlayers);
        Assert.Equal(12, restored.MaxPlayers);
        Assert.True(restored.PlayerRangeDefaulted);
    }
}
