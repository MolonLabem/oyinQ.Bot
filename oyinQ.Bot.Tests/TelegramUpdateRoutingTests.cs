using System.Net;
using System.Text;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

namespace oyinQ.Bot.Tests;

public sealed class TelegramUpdateRoutingTests
{
    [Theory]
    [InlineData("/start", "/start")]
    [InlineData("/help", "/help")]
    [InlineData("/HELP@OyinQBot", "/help")]
    [InlineData("/menu", "/menu")]
    [InlineData("/START@OyinQBot community-club", "/start")]
    [InlineData("plain text", null)]
    public void GetCommand_NormalizesTelegramCommands(string text, string? expected) =>
        Assert.Equal(expected, TelegramUpdateRouting.GetCommand(text, "OyinQBot"));

    [Theory]
    [InlineData("/oiynq", true)]
    [InlineData("/oyinq", true)]
    [InlineData("/OYINQ@CurrentBot", true)]
    [InlineData("/OIYNQ@CurrentBot", true)]
    [InlineData("/oiynq@OtherBot", false)]
    [InlineData("/oyinq@OtherBot", false)]
    [InlineData("/start", false)]
    [InlineData("/menu", false)]
    [InlineData("/admin", false)]
    [InlineData("/games", false)]
    [InlineData("/addgame", false)]
    [InlineData("/wanted", false)]
    [InlineData("/mygames", false)]
    public void GroupEntry_OnlyKeepsOyinQCommand(string text, bool expected) =>
        Assert.Equal(expected, TelegramUpdateRouting.IsGroupEntryRequest(text, TelegramUpdateRouting.GetCommand(text, "CurrentBot")));

    [Theory]
    [InlineData("/oiynq topic", true)]
    [InlineData("/oyinq topic", true)]
    [InlineData("/OIYNQ@CurrentBot TOPIC", true)]
    [InlineData("/oiynq@OtherBot topic", false)]
    [InlineData("/oyinq@OtherBot topic", false)]
    [InlineData("/oiynq", false)]
    [InlineData("/oiynq other", false)]
    [InlineData("/oiynq topic extra", false)]
    public void PostingTopicSelection_UsesOyinQSubcommand(string text, bool expected) =>
        Assert.Equal(expected, TelegramUpdateRouting.IsPostingTopicSelectionRequest(
            text, TelegramUpdateRouting.GetCommand(text, "CurrentBot")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OtherBot")]
    public void AddressedCommandsRequireMatchingKnownBotUsername(string? username) =>
        Assert.Null(TelegramUpdateRouting.GetCommand("/oiynq@CurrentBot topic", username));

    [Theory]
    [InlineData("/oiynq topic", "/oiynq", 0)]
    [InlineData("plain text", null, 0)]
    [InlineData("/OIYNQ@renamedbot topic", "/oiynq", 1)]
    [InlineData("/oyinq@RenamedBot", "/oyinq", 1)]
    [InlineData("/oiynq@OtherBot topic", null, 1)]
    [InlineData("/start@OldBot community-club", null, 1)]
    [InlineData("/help@RenamedBot", "/help", 1)]
    public async Task CommandRoutingUsesRuntimeBotIdentityOnlyForAddressedCommands(string text, string? expected, int identityRequests)
    {
        using var handler = new BotIdentityHandler();
        using var http = new HttpClient(handler);
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", http);
        Assert.Equal(expected, await TelegramUpdateRouting.GetCommandAsync(text, bot, default));
        Assert.Equal(identityRequests, handler.Calls);
    }

    private sealed class BotIdentityHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.EndsWith("/getMe", request.RequestUri!.AbsolutePath);
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"result":{"id":123456,"is_bot":true,"first_name":"OyinQ","username":"RenamedBot"}}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
