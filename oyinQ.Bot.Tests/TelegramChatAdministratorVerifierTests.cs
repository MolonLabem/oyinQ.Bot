using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

namespace oyinQ.Bot.Tests;

public sealed class TelegramChatAdministratorVerifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden: bot was kicked from the supergroup chat")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden: bot is not a member of the supergroup chat")]
    [InlineData(HttpStatusCode.BadRequest, "Bad Request: chat not found")]
    public async Task UnavailableChat_IsPersistedWithoutThrowing(HttpStatusCode status, string description)
    {
        await using var fixture = new Fixture(status,
            $"{{\"ok\":false,\"error_code\":{(int)status},\"description\":\"{description}\"}}");
        fixture.AddKnownChat(isBotPresent: true);

        Assert.False(await fixture.Verifier.IsAdministratorAsync(-1001, 42, default));
        Assert.False(fixture.Db.KnownTelegramChats.Single().IsBotPresent);
        Assert.Equal(1, fixture.Handler.RequestCount);
    }

    [Fact]
    public async Task SuccessfulCheck_RestoresKnownBotPresence()
    {
        const string response =
            "{\"ok\":true,\"result\":{\"user\":{\"id\":42,\"is_bot\":false,\"first_name\":\"Admin\"},\"status\":\"administrator\",\"can_be_edited\":false,\"can_manage_chat\":true,\"can_delete_messages\":true,\"can_manage_video_chats\":true,\"can_restrict_members\":true,\"can_promote_members\":false,\"can_change_info\":true,\"can_invite_users\":true,\"can_post_stories\":false,\"can_edit_stories\":false,\"can_delete_stories\":false,\"is_anonymous\":false}}";
        await using var fixture = new Fixture(HttpStatusCode.OK, response);
        fixture.AddKnownChat(isBotPresent: false);

        Assert.True(await fixture.Verifier.IsAdministratorAsync(-1001, 42, default));
        Assert.True(fixture.Db.KnownTelegramChats.Single().IsBotPresent);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(HttpStatusCode status, string response)
        {
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Handler = new ResponseHandler(status, response);
            var bot = new TelegramBotClient(
                "123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", new HttpClient(Handler));
            Verifier = new TelegramChatAdministratorVerifier(Db, bot, TimeProvider.System,
                NullLogger<TelegramChatAdministratorVerifier>.Instance);
        }

        public AppDbContext Db { get; }
        public ResponseHandler Handler { get; }
        public TelegramChatAdministratorVerifier Verifier { get; }

        public void AddKnownChat(bool isBotPresent)
        {
            var now = DateTimeOffset.UtcNow;
            Db.KnownTelegramChats.Add(new KnownTelegramChat
            {
                TelegramChatId = -1001,
                IsBotPresent = isBotPresent,
                FirstSeenAt = now,
                UpdatedAt = now
            });
            Db.SaveChanges();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class ResponseHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
