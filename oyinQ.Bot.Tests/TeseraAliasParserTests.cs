using oyinQ.Bot.Integrations.Tesera;

namespace oyinQ.Bot.Tests;

public sealed class TeseraAliasParserTests
{
    [Theory]
    [InlineData("alias", "alias")]
    [InlineData("tesera.ru/user/alias", "alias")]
    [InlineData("https://tesera.ru/users/alias/", "alias")]
    [InlineData("https://tesera.ru/user/%D1%82%D0%B5%D1%81%D1%82", "тест")]
    public void Parse_SupportedInputs_ReturnsAlias(string input, string expected) =>
        Assert.Equal(expected, TeseraAliasParser.Parse(input));
}
