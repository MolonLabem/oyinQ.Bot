using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace oyinQ.Bot.Tests;

public sealed class TelegramCommunityMembershipVerifierTests
{
    [Fact]
    public async Task RecentlyUnavailableChat_IsRejectedWithoutCallingTelegram()
    {
        await using var fixture = new Fixture(HttpStatusCode.OK, MemberResponse);
        fixture.AddKnownChat(isBotPresent: false, DateTimeOffset.UtcNow);

        Assert.False(await fixture.Verifier.IsMemberAsync(-1001, 42, default));
        Assert.Equal(0, fixture.Handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden: bot was kicked from the supergroup chat")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden: bot is not a member of the supergroup chat")]
    [InlineData(HttpStatusCode.BadRequest, "Bad Request: chat not found")]
    public async Task UnavailableChat_IsPersistedAndRejected(HttpStatusCode status, string description)
    {
        await using var fixture = new Fixture(status,
            $"{{\"ok\":false,\"error_code\":{(int)status},\"description\":\"{description}\"}}");
        fixture.AddKnownChat(isBotPresent: true, DateTimeOffset.UtcNow);

        Assert.False(await fixture.Verifier.IsMemberAsync(-1001, 42, default));
        Assert.False(fixture.Db.KnownTelegramChats.Single().IsBotPresent);
        Assert.Equal(1, fixture.Handler.RequestCount);
    }

    [Fact]
    public async Task StaleUnavailableChat_IsProbedAndRecoversAfterBotIsReadded()
    {
        await using var fixture = new Fixture(HttpStatusCode.OK, MemberResponse);
        fixture.AddKnownChat(isBotPresent: false, DateTimeOffset.UtcNow.AddHours(-1));

        Assert.True(await fixture.Verifier.IsMemberAsync(-1001, 42, default));
        Assert.True(fixture.Db.KnownTelegramChats.Single().IsBotPresent);
        Assert.Equal(1, fixture.Handler.RequestCount);
    }

    [Fact]
    public void UnrelatedTelegramFailure_IsNotClassifiedAsMissingChat()
    {
        Assert.False(TelegramCommunityMembershipVerifier.IsChatUnavailable(
            new ApiRequestException("Internal Server Error", 500)));
    }

    private const string MemberResponse =
        "{\"ok\":true,\"result\":{\"user\":{\"id\":42,\"is_bot\":false,\"first_name\":\"User\"},\"status\":\"member\"}}";

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(HttpStatusCode status, string response)
        {
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Handler = new ResponseHandler(status, response);
            var bot = new TelegramBotClient(
                "123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", new HttpClient(Handler));
            Verifier = new TelegramCommunityMembershipVerifier(Db, bot, TimeProvider.System,
                NullLogger<TelegramCommunityMembershipVerifier>.Instance);
        }

        public AppDbContext Db { get; }
        public ResponseHandler Handler { get; }
        public TelegramCommunityMembershipVerifier Verifier { get; }

        public void AddKnownChat(bool isBotPresent, DateTimeOffset updatedAt)
        {
            Db.KnownTelegramChats.Add(new KnownTelegramChat
            {
                TelegramChatId = -1001,
                IsBotPresent = isBotPresent,
                FirstSeenAt = updatedAt,
                UpdatedAt = updatedAt
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
