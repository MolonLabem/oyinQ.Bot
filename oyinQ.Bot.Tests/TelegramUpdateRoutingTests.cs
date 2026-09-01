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
    [InlineData("/oyinq", true)]
    [InlineData("/OYINQ@CurrentBot", true)]
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
    [InlineData("/oyinq topic", true)]
    [InlineData("/OYINQ@CurrentBot TOPIC", true)]
    [InlineData("/oyinq", false)]
    [InlineData("/oyinq other", false)]
    [InlineData("/oyinq topic extra", false)]
    public void PostingTopicSelection_UsesOyinQSubcommand(string text, bool expected) =>
        Assert.Equal(expected, TelegramUpdateRouting.IsPostingTopicSelectionRequest(
            text, TelegramUpdateRouting.GetCommand(text)));
}
