using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

namespace oyinQ.Bot.Tests;

public sealed class TelegramStartupServiceTests
{
    [Fact]
    public async Task ProfileSetup_ContinuesStartupAfterBoundedTelegramFailures()
    {
        var handler = new FailingHandler();
        var bot = Bot(handler);
        var links = new MiniAppLinkBuilder(Options.Create(new BotOptions
        {
            PublicBaseUrl = "https://example.test"
        }));
        var service = new TelegramBotProfileSetupService(bot, links,
            NullLogger<TelegramBotProfileSetupService>.Instance);

        await service.StartAsync(default);

        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task WebhookSetup_ContinuesStartupAfterBoundedTelegramFailures()
    {
        var handler = new FailingHandler();
        var options = Options.Create(new BotOptions
        {
            PublicBaseUrl = "https://example.test",
            WebhookSecret = "secret"
        });
        var service = new TelegramWebhookSetupService(Bot(handler), options,
            NullLogger<TelegramWebhookSetupService>.Instance);

        await service.StartAsync(default);

        Assert.Equal(3, handler.RequestCount);
    }

    private static TelegramBotClient Bot(HttpMessageHandler handler) => new(
        "123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", new HttpClient(handler));

    private sealed class FailingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
