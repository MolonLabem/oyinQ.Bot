using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class PlayerCountRangeTests
{
    [Fact]
    public void SharedUiContract_CoversSelectedExpansionsAndIncompleteMetadata()
    {
        var cases = System.Text.Json.JsonSerializer.Deserialize<RangeCase[]>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game-player-ranges.json")),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        foreach (var item in cases)
        {
            var range = PlayerCountRange.Normalize(item.Minimum, item.Maximum)
                .WithExpansions(GatheringExpansionSelection.Select(item.Expansions, item.Selected));
            Assert.Equal((item.ExpectedMinimum, item.ExpectedMaximum, item.Defaulted),
                (range.Minimum, range.Maximum, range.WasDefaulted));
        }
    }

    private sealed record RangeCase(int? Minimum, int? Maximum, ClubCollectionExpansion[] Expansions,
        long[] Selected, int ExpectedMinimum, int ExpectedMaximum, bool Defaulted);

    [Fact]
    public void CreateAndEdit_UseSelectedRange_AndCannotRemoveFifthPlayerExpansionWithFiveSeatsOccupied()
    {
        var game = new ClubCollectionGame(10, "Цивилизация", null, null, 2, 4, null, [new(20, "Дополнение", MinPlayers: 2, MaxPlayers: 5)]);
        var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        var without = GatheringGameSnapshot.FromClubGame(game, []);
        Assert.Throws<InvalidOperationException>(() => GatheringRules.Create("club", without, 1, now.AddDays(1), 2, 5, 5, null, true, now));
        var gathering = GatheringRules.Create("club", GatheringGameSnapshot.FromClubGame(game, [20]), 1, now.AddDays(1), 2, 5, 5, null, true, now);
        Assert.Equal(5, GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson).MaxPlayers);
        GatheringRules.Update(gathering, now.AddDays(1), 2, 4, 4, null, true, [], now);
        Assert.Equal(4, GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson).MaxPlayers);
        GatheringRules.Update(gathering, now.AddDays(1), 2, 5, 5, null, true, [20], now);
        for (var i = 0; i < 4; i++) gathering.Guests.Add(new() { DisplayName = $"Гость {i}" });
        Assert.Throws<InvalidOperationException>(() => GatheringRules.Update(gathering, now.AddDays(1), 2, 4, 4, null, true, [], now));
        Assert.Throws<InvalidOperationException>(() => GatheringRules.Update(gathering, now.AddDays(1), 2, 5, 5, null, true, [], now));
        Assert.Equal(5, gathering.MaximumPlayers);
        Assert.Equal(20, Assert.Single(GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson).SelectedExpansions).BggId);
    }

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
