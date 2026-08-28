using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggGameUrlParserTests
{
    [Theory]
    [InlineData("https://boardgamegeek.com/boardgame/167791/terraforming-mars", 167791)]
    [InlineData("http://www.boardgamegeek.com/boardgame/13", 13)]
    [InlineData("boardgamegeek.com/boardgame/42/name?foo=bar", 42)]
    public void Parse_ValidGameLink_ReturnsId(string value, long expected) =>
        Assert.Equal(expected, BggGameUrlParser.Parse(value));

    [Theory]
    [InlineData("https://evil.example/boardgame/13")]
    [InlineData("https://boardgamegeek.com/boardgame/not-a-number")]
    [InlineData("13")]
    public void Parse_InvalidInput_ReturnsNull(string value) =>
        Assert.Null(BggGameUrlParser.Parse(value));
}
