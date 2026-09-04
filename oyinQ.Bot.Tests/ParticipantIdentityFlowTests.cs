using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class ParticipantIdentityFlowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task FreshAuthenticatedUser_CanViewAndJoinClub_Idempotently()
    {
        await using var db = CreateDb();
        var gathering = await SeedAsync(db, BotMode.Club);
        Assert.False(await db.Participants.AnyAsync(x => x.TelegramUserId == 42));
        await ThroughFilter(db, async _ =>
        {
            var viewer = await db.Participants.SingleAsync(x => x.TelegramUserId == 42);
            Assert.Null(viewer.PrivateChatStartedAt);
            Assert.True(GatheringAccessPolicy.CanJoin(gathering, false, false, Now));
            return Results.Ok(gathering.PublicId);
        });
        var service = new GatheringService(db, new CampParticipationPolicy(db, TimeProvider.System));
        for (var i = 0; i < 2; i++)
            await ThroughFilter(db, async _ =>
            { await service.JoinAsync(gathering.PublicId, "community", 42, Now, default); return Results.NoContent(); });
        Assert.Single(await db.Participants.Where(x => x.TelegramUserId == 42).ToArrayAsync());
        Assert.Single(gathering.Participants);
    }

    [Fact]
    public async Task RefreshPreservesPreferredNameContextAndPrivateStartCapability()
    {
        await using var db = CreateDb();
        var identity = new ParticipantIdentityService(db, TimeProvider.System);
        var row = await identity.GetOrCreateAsync(42, "old", "Старое имя", "club-a", default);
        row.PreferredDisplayName = "Моё имя";
        await db.SaveChangesAsync();
        await ThroughFilter(db, _ => ValueTask.FromResult<object?>(Results.Ok()));
        Assert.Equal("trusted", row.TelegramUsername);
        Assert.Equal("Новое имя", row.DisplayName);
        Assert.Equal("Моё имя", row.PreferredDisplayName);
        Assert.Equal("club-a", row.ActiveCommunityKey);
        Assert.Null(row.PrivateChatStartedAt);
        await identity.GetOrCreateAsync(42, "trusted", "Новое имя", null, default, privateMessageReceived: true);
        var started = row.PrivateChatStartedAt;
        Assert.NotNull(started);
        await ThroughFilter(db, _ => ValueTask.FromResult<object?>(Results.Ok()));
        Assert.Equal(started, row.PrivateChatStartedAt);
    }

    [Fact]
    public async Task FreshCampUser_IsProvisionedButRegistrationStillRequired()
    {
        await using var db = CreateDb();
        var gathering = await SeedAsync(db, BotMode.Camp);
        var service = new GatheringService(db, new CampParticipationPolicy(db, TimeProvider.System));
        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await ThroughFilter(db, async _ =>
            { await service.JoinAsync(gathering.PublicId, "community", 42, Now, default); return Results.NoContent(); }));
        Assert.Contains("регистрацию", error.Message);
        Assert.True(await db.Participants.AnyAsync(x => x.TelegramUserId == 42));
        Assert.Empty(gathering.Participants);
    }

    [Theory]
    [InlineData(GatheringStatus.Cancelled)]
    [InlineData(GatheringStatus.Completed)]
    [InlineData(GatheringStatus.Closed)]
    public async Task ProvisioningDoesNotBypassGatheringAccess(GatheringStatus status)
    {
        await using var db = CreateDb();
        var gathering = await SeedAsync(db, BotMode.Club);
        gathering.Status = status;
        await db.SaveChangesAsync();
        Assert.False(GatheringAccessPolicy.CanJoin(gathering, false, false, Now));
        var service = new GatheringService(db, new CampParticipationPolicy(db, TimeProvider.System));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ThroughFilter(db, async _ =>
        { await service.JoinAsync(gathering.PublicId, "community", 42, Now, default); return Results.NoContent(); }));
        Assert.Empty(gathering.Participants);
    }

    [Fact]
    public async Task InvalidAuthenticationDoesNotProvisionOrCallEndpoint()
    {
        await using var db = CreateDb();
        var invoked = false;
        await ThroughFilter(db, _ => { invoked = true; return ValueTask.FromResult<object?>(null); }, signed: false);
        Assert.False(invoked);
        Assert.Empty(await db.Participants.ToArrayAsync());
    }

    [Fact]
    public void PrivateStartRoundTripRetainsExactGatheringAndRuntimeUsername()
    {
        var id = Guid.NewGuid();
        var parameter = MiniAppStartParameter.ForGathering("club-a", id);
        Assert.Equal(new MiniAppStartContext("club-a", id), MiniAppStartParameter.Parse($"/start {parameter}"));
        Assert.StartsWith("https://t.me/RuntimeBot?start=", TelegramBotDeepLinks.BuildStart("RuntimeBot", parameter));
        Assert.Contains("/oiynq", TelegramEntryText.CommunityOnboarding);
        Assert.DoesNotContain("/oyinq", TelegramEntryText.CommunityOnboarding);
    }

    [Fact]
    public async Task PrivateStartCtaResolvesRuntimeBotAndDisappearsOnlyAfterPrivateMessage()
    {
        using var handler = new GetMeHandler();
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", new HttpClient(handler));
        var capability = new PrivateChatCapability(bot, NullLogger<PrivateChatCapability>.Instance);
        var participant = ParticipantIdentityPolicy.Create(42, null, "Игрок", Now);
        var gathering = Guid.NewGuid();
        var url = await capability.StartUrlAsync(participant, "club", gathering, default);
        Assert.Equal(TelegramBotDeepLinks.BuildStart("ActualRuntimeBot", MiniAppStartParameter.ForGathering("club", gathering)), url);
        Assert.Equal(1, handler.Calls);
        Assert.Null(participant.PrivateChatStartedAt);
        participant.PrivateChatStartedAt = Now;
        Assert.Null(await capability.StartUrlAsync(participant, "club", gathering, default));
        Assert.Equal(1, handler.Calls);
    }

    private sealed class GetMeHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.EndsWith("/getMe", request.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("""{"ok":true,"result":{"id":123456,"is_bot":true,"first_name":"OyinQ","username":"ActualRuntimeBot"}}""", Encoding.UTF8, "application/json") });
        }
    }

    private static async Task<object?> ThroughFilter(AppDbContext db, EndpointFilterDelegate next, bool signed = true)
    {
        var services = new ServiceCollection().AddSingleton(db).AddSingleton<TimeProvider>(TimeProvider.System)
            .AddSingleton(Options.Create(new BotOptions { Token = "123:test" }))
            .AddTransient<TelegramMiniAppAuthenticator>().AddTransient<ParticipantIdentityService>().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers["X-Telegram-Init-Data"] = signed ? SignedData() : "invalid";
        return await new MiniAppIdentityFilter().InvokeAsync(EndpointFilterInvocationContext.Create(context), next);
    }

    private static string SignedData()
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        { ["auth_date"] = Now.ToUnixTimeSeconds().ToString(),
          ["user"] = JsonSerializer.Serialize(new { id = 42, first_name = "Новое имя", username = "trusted" }) };
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes("123:test"));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', values.Select(x => $"{x.Key}={x.Value}"))));
        return string.Join('&', values.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")) + "&hash=" + Convert.ToHexStringLower(hash);
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static async Task<GameGathering> SeedAsync(AppDbContext db, BotMode mode)
    {
        var organizer = ParticipantIdentityPolicy.Create(100, null, "Организатор", Now);
        var community = new OyinQCommunity { Key = "community", Name = "Сообщество", Mode = mode,
            TimeZoneId = "UTC", IsActive = true };
        if (mode == BotMode.Camp) community.Camp = new Camp { BotChatKey = community.Key, Name = "Кэмп",
            Status = CampStatus.Active, StartsAtUtc = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero),
            EndsAtUtc = new DateTimeOffset(Now.AddDays(4).UtcDateTime.Date, TimeSpan.Zero) };
        db.AddRange(organizer, community);
        await db.SaveChangesAsync();
        var gathering = new GameGathering { PublicId = Guid.NewGuid(), CommunityKey = community.Key,
            OrganizerParticipant = organizer, OrganizerParticipantId = organizer.Id, StartsAtUtc = Now.AddDays(1),
            MinimumPlayers = 2, DesiredPlayers = 4, MaximumPlayers = 4, Status = GatheringStatus.Recruiting,
            GameSnapshotJson = "{}" };
        db.Add(gathering); await db.SaveChangesAsync(); return gathering;
    }
}
