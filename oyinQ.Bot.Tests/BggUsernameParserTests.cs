using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggUsernameParserTests
{
    [Theory]
    [InlineData("john_doe", "john_doe")]
    [InlineData("boardgamegeek.com/user/john_doe", "john_doe")]
    [InlineData("https://boardgamegeek.com/collection/user/john_doe", "john_doe")]
    [InlineData("https://boardgamegeek.com/collection?username=john_doe", "john_doe")]
    public void Parse_SupportedInputs_ReturnsUsername(string input, string expected) =>
        Assert.Equal(expected, BggUsernameParser.Parse(input));

    [Fact]
    public void Parse_UrlWithSpaces_ReturnsNull() =>
        Assert.Null(BggUsernameParser.Parse("https://boardgamegeek.com/user/john doe"));
}
