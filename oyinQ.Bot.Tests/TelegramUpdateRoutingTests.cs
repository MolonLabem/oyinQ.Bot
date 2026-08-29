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
    [InlineData("/start", true)]
    [InlineData("/menu", true)]
    [InlineData("/admin", true)]
    [InlineData("/games", false)]
    [InlineData("/addgame", false)]
    [InlineData("/wanted", false)]
    [InlineData("/mygames", false)]
    public void GroupEntry_OnlyKeepsCurrentNativeCommands(string text, bool expected) =>
        Assert.Equal(expected, TelegramUpdateRouting.IsGroupEntryRequest(text, TelegramUpdateRouting.GetCommand(text)));
}
