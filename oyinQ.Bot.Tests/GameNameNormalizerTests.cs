using oyinQ.Bot.Common.Normalization;

namespace oyinQ.Bot.Tests;

public sealed class GameNameNormalizerTests
{
    private readonly GameNameNormalizer _sut = new();

    [Theory]
    [InlineData(" Catan ", "catan")]
    [InlineData("Ticket TO Ride", "ticket to ride")]
    [InlineData("7 Wonders: Duel", "7 wonders duel")]
    [InlineData("  Dune   Imperium  ", "dune imperium")]
    [InlineData("Ark Nova!", "ark nova")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_ReturnsExpectedValue(string input, string expected)
    {
        Assert.Equal(expected, _sut.Normalize(input));
    }
}
