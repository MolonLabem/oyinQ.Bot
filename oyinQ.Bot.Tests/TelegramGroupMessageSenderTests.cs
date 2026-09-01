using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Tests;

public sealed class TelegramGroupMessageSenderTests
{
    [Fact]
    public async Task GatheringStyleMessage_UsesConfiguredThreadId()
    {
        await using var fixture = new Fixture(configuredThreadId: 42);

        await fixture.Sender.SendMessageAsync("club", "Сбор", ParseMode.Html, null, default);

        var body = Assert.Single(fixture.Handler.Bodies);
        Assert.Contains("\"chat_id\":-1001", body);
        Assert.Contains("\"message_thread_id\":42", body);
    }

    [Fact]
    public async Task GroupWithoutConfiguredThread_PostsAsBefore()
    {
        await using var fixture = new Fixture();

        await fixture.Sender.SendMessageAsync("club", "Сбор", ParseMode.Html, null, default);

        Assert.DoesNotContain("message_thread_id", Assert.Single(fixture.Handler.Bodies));
    }

    [Fact]
    public async Task ThreadNotFound_ClearsConfigurationAndRetriesOnlyInDefaultChat()
    {
        await using var fixture = new Fixture(configuredThreadId: 42, failFirstAsMissingThread: true);

        await fixture.Sender.SendMessageAsync("club", "Сбор", ParseMode.Html, null, default);

        Assert.Equal(2, fixture.Handler.Bodies.Count);
        Assert.Contains("\"message_thread_id\":42", fixture.Handler.Bodies[0]);
        Assert.DoesNotContain("message_thread_id", fixture.Handler.Bodies[1]);
        var community = fixture.Db.OyinQCommunities.Single();
        Assert.Null(community.PostingMessageThreadId);
        Assert.NotNull(community.PostingTopicInvalidatedAt);
        Assert.True(fixture.Db.TelegramForumTopics.Single().IsDeleted);
    }

    [Theory]
    [InlineData("Bad Request: message thread not found", 400, true)]
    [InlineData("Bad Request: TOPIC_CLOSED", 400, true)]
    [InlineData("Forbidden: bot is not a member of the supergroup chat", 403, false)]
    [InlineData("Bad Request: chat not found", 400, false)]
    public void FallbackClassification_IsRestrictedToTopicErrors(string message, int code, bool expected)
    {
        Assert.Equal(expected, TelegramGroupMessageSender.IsUnavailableThread(
            new Telegram.Bot.Exceptions.ApiRequestException(message, code)));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(int? configuredThreadId = null, bool failFirstAsMissingThread = false)
        {
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Db.OyinQCommunities.Add(new OyinQCommunity
            {
                Key = "club", Name = "Клуб", TelegramChatId = -1001, Mode = BotMode.Club,
                TimeZoneId = "UTC", PostingMessageThreadId = configuredThreadId,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            Db.KnownTelegramChats.Add(new KnownTelegramChat
            {
                TelegramChatId = -1001, IsForum = true, IsBotPresent = true,
                FirstSeenAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            if (configuredThreadId is { } threadId)
                Db.TelegramForumTopics.Add(new TelegramForumTopic
                {
                    TelegramChatId = -1001, MessageThreadId = threadId, Name = "Сборы",
                    LastSeenAt = DateTimeOffset.UtcNow
                });
            Db.SaveChanges();
            Handler = new RecordingHandler(failFirstAsMissingThread);
            var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO",
                new HttpClient(Handler));
            var botOptions = Options.Create(new BotOptions { PublicBaseUrl = "https://example.test" });
            Sender = new TelegramGroupMessageSender(Db, bot,
                Options.Create(new AdministrationOptions()), new MiniAppLinkBuilder(botOptions),
                TimeProvider.System, NullLogger<TelegramGroupMessageSender>.Instance);
        }

        public AppDbContext Db { get; }
        public RecordingHandler Handler { get; }
        public TelegramGroupMessageSender Sender { get; }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingHandler(bool failFirst) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            if (failFirst && Bodies.Count == 1)
                return Json(HttpStatusCode.BadRequest,
                    "{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: message thread not found\"}");
            return Json(HttpStatusCode.OK,
                "{\"ok\":true,\"result\":{\"message_id\":123,\"date\":0,\"chat\":{\"id\":-1001,\"type\":\"supergroup\"}}}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
