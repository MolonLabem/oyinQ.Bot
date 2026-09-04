using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class TelegramUpdateRoutingTests
{
    [Theory]
    [InlineData("/start", "/start")]
    [InlineData("/START@OyinQBot community-club", "/start")]
    [InlineData("plain text", null)]
    public void GetCommand_NormalizesTelegramCommands(string text, string? expected) =>
        Assert.Equal(expected, TelegramUpdateRouting.GetCommand(text));

    [Theory]
    [InlineData("/oiynq", true)]
    [InlineData("/oyinq", true)]
    [InlineData("/OYINQ@CurrentBot", true)]
    [InlineData("/OIYNQ@CurrentBot", true)]
    [InlineData("/start", false)]
    [InlineData("/menu", false)]
    [InlineData("/admin", false)]
    [InlineData("/games", false)]
    [InlineData("/addgame", false)]
    [InlineData("/wanted", false)]
    [InlineData("/mygames", false)]
    public void GroupEntry_OnlyKeepsOyinQCommand(string text, bool expected) =>
        Assert.Equal(expected, TelegramUpdateRouting.IsGroupEntryRequest(text, TelegramUpdateRouting.GetCommand(text)));

    [Theory]
    [InlineData("/oiynq topic", true)]
    [InlineData("/oyinq topic", true)]
    [InlineData("/OIYNQ@CurrentBot TOPIC", true)]
    [InlineData("/oiynq", false)]
    [InlineData("/oiynq other", false)]
    [InlineData("/oiynq topic extra", false)]
    public void PostingTopicSelection_UsesOyinQSubcommand(string text, bool expected) =>
        Assert.Equal(expected, TelegramUpdateRouting.IsPostingTopicSelectionRequest(
            text, TelegramUpdateRouting.GetCommand(text)));
}
