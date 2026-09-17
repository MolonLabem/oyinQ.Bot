using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggBestPlayerRecommendationTests
{
    [Theory]
    [InlineData("4", 4, true)]
    [InlineData("14", 4, false)]
    [InlineData("2–4", 2, true)]
    [InlineData("2–4", 3, true)]
    [InlineData("2–4", 4, true)]
    [InlineData("2–4", 5, false)]
    [InlineData("2-4", 3, true)]
    [InlineData("2—4", 3, true)]
    [InlineData(" 2 – 4 , 6, 8–10 ", 4, true)]
    [InlineData("2–4, 5", 5, true)]
    [InlineData("2–4, 6, 8–10", 6, true)]
    [InlineData("2–4, 6, 8–10", 9, true)]
    [InlineData("2–4, 6, 8–10", 7, false)]
    [InlineData(null, 4, false)]
    [InlineData("", 4, false)]
    [InlineData(" ", 4, false)]
    [InlineData("unknown", 4, false)]
    [InlineData("4, unknown", 4, false)]
    [InlineData("4,", 4, false)]
    [InlineData("4,,6", 4, false)]
    [InlineData("4–2", 3, false)]
    [InlineData("2–4–6", 4, false)]
    [InlineData("2–", 2, false)]
    [InlineData("-4", 4, false)]
    [InlineData("0–4", 4, false)]
    [InlineData("4.0", 4, false)]
    [InlineData("4+", 4, false)]
    [InlineData("2147483648", 4, false)]
    [InlineData("4", 0, false)]
    [InlineData("4", -4, false)]
    public void Matches_ParsesTheWholeRecommendation(string? value, int players, bool expected) =>
        Assert.Equal(expected, BggBestPlayerRecommendation.Matches(value, players));

    [Fact]
    public void Matches_ReadsCalculatorOutputWithoutChangingItsFormat()
    {
        var value = BggBestPlayerCalculator.Collapse(["2", "3", "4", "6", "8", "9"]);
        Assert.Equal("2–4, 6, 8–9", value);
        Assert.True(BggBestPlayerRecommendation.Matches(value, 3));
        Assert.True(BggBestPlayerRecommendation.Matches(value, 6));
        Assert.False(BggBestPlayerRecommendation.Matches(value, 5));
    }
}
