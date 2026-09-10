using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace oyinQ.Bot.Tests;

public sealed class TelegramCommunityMembershipVerifierTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestrictedStatusRequiresCurrentMembership(bool isMember)
    {
        var response = "{\"ok\":true,\"result\":{\"status\":\"restricted\",\"user\":{\"id\":42,\"is_bot\":false,\"first_name\":\"User\"},\"is_member\":"
            + isMember.ToString().ToLowerInvariant() + ",\"until_date\":0}}";
        await using var fixture = new Fixture(HttpStatusCode.OK, response);
        Assert.Equal(isMember, await fixture.Verifier.IsMemberAsync(-1001, 42, default));
        fixture.Db.OyinQCommunities.Add(new() { Key = "club", Name = "Club", TelegramChatId = -1001,
            TimeZoneId = "UTC", IsActive = true });
        await fixture.Db.SaveChangesAsync();
        var resolver = new CommunityContextResolver(new CommunityStore(fixture.Db), fixture.Verifier);
        Assert.Equal(isMember, await resolver.ResolveAuthorizedAsync("club", 42, default) is not null);
        Assert.Equal(isMember ? 1 : 0, (await resolver.ResolveAuthorizedAsync(42, default)).Count);
    }

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidParticipant_DoesNotMarkChatUnavailableOrBlockOtherMembers(bool knownChat)
    {
        await using var fixture = new Fixture(HttpStatusCode.BadRequest,
            "{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: PARTICIPANT_ID_INVALID\"}");
        var updatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        if (knownChat) fixture.AddKnownChat(true, updatedAt);

        Assert.False(await fixture.Verifier.IsMemberAsync(-1001, 42, default));
        if (knownChat)
        {
            var chat = Assert.Single(fixture.Db.KnownTelegramChats);
            Assert.True(chat.IsBotPresent);
            Assert.Equal(updatedAt, chat.UpdatedAt);
        }
        else Assert.Empty(fixture.Db.KnownTelegramChats);

        fixture.Handler.Status = HttpStatusCode.OK;
        fixture.Handler.Response = MemberResponse;
        Assert.True(await fixture.Verifier.IsMemberAsync(-1001, 99, default));
        Assert.Equal(2, fixture.Handler.RequestCount);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(429)]
    [InlineData(0)]
    public async Task TemporaryFailure_DoesNotChangeChatStateAndAllowsNextProbe(int errorCode)
    {
        await using var fixture = new Fixture((HttpStatusCode)errorCode,
            $"{{\"ok\":false,\"error_code\":{errorCode},\"description\":\"Temporary failure\"}}");
        var updatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        fixture.AddKnownChat(true, updatedAt);
        if (errorCode == 0) fixture.Handler.Failure = new HttpRequestException("Connection reset by peer");

        await Assert.ThrowsAsync<CommunityMembershipUnavailableException>(
            () => fixture.Verifier.IsMemberAsync(-1001, 42, default));
        var chat = Assert.Single(fixture.Db.KnownTelegramChats);
        Assert.True(chat.IsBotPresent);
        Assert.Equal(updatedAt, chat.UpdatedAt);

        fixture.Handler.Failure = null;
        fixture.Handler.Status = HttpStatusCode.OK;
        fixture.Handler.Response = MemberResponse;
        Assert.True(await fixture.Verifier.IsMemberAsync(-1001, 42, default));
    }

    [Fact]
    public async Task RequestCancellation_IsNotReportedAsTemporaryFailure()
    {
        await using var fixture = new Fixture(HttpStatusCode.OK, MemberResponse);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Verifier.IsMemberAsync(-1001, 42, cancellation.Token));
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
        public HttpStatusCode Status { get; set; } = status;
        public string Response { get; set; } = response;
        public Exception? Failure { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (Failure is { } failure) throw failure;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/json")
            });
        }
    }
}
