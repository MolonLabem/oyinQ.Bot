using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Net;
using System.Text;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task GameFilterPrecedesPaginationAndPreservesScope()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        await using var db = database.Open(); var community = (await db.OyinQCommunities.SingleAsync()).ToBotCommunity();
        var target = await Management(db, new Bgg()).CreateAsync(community, new(actor.TelegramUserId), Command(), default);
        var other = GatheringRules.Create("club", Snapshot() with { BggId = 99 }, actor.Id, Now.AddMinutes(30), 1, 2, 4, null, false, Now);
        db.GameGatherings.Add(other); await db.SaveChangesAsync();
        var rows = await GatheringListQuery.Apply(GatheringListQuery.ForGame(db, "club", 42), GatheringListScope.Upcoming, Now).Take(1).ToArrayAsync();
        Assert.Equal(target.PublicId, Assert.Single(rows).PublicId);
        Assert.Empty(await GatheringListQuery.ForGame(db, "other", 42).ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task ConcurrentPublicationSendsOnceAndQueuesEditsCommittedDuringSend()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        Guid id;
        await using (var seed = database.Open()) id = (await Management(seed, new Bgg()).CreateAsync((await seed.OyinQCommunities.SingleAsync()).ToBotCommunity(), new(actor.TelegramUserId), Command(), default)).PublicId;
        var entered = Signal(); var resume = Signal(); var sender = new PublicationSender(entered, resume);
        using var handler = new PublicationBotHandler();
        using var http = new HttpClient(handler);
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", http);
        GatheringPublicationService Service(oyinQ.Bot.Data.AppDbContext db) => new(db, new CommunityStore(db),
            new(sender, bot, new GatheringPresentationService(), NullLogger<GatheringTelegramPublisher>.Instance), Time, NullLogger<GatheringPublicationService>.Instance);
        await using var first = database.Open(); await using var second = database.Open();
        var sending = Service(first).PublishAsync(id, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.False(await Service(second).PublishAsync(id, default));
            await using var mutation = database.Open();
            var row = await mutation.GameGatherings.SingleAsync();
            GatheringPublication.Request(row); row.Description = "Новые условия"; await mutation.SaveChangesAsync();
        }
        finally { resume.TrySetResult(); }
        Assert.True(await sending);
        await using var verify = database.Open();
        var saved = await verify.GameGatherings.SingleAsync();
        Assert.Equal(oyinQ.Bot.Data.Entities.GatheringPublicationStatus.Pending, saved.PublicationStatus);
        Assert.Equal(123, saved.TelegramMessageId);
        Assert.True(await Service(verify).PublishAsync(id, default));
        Assert.Equal(1, sender.Calls); Assert.Equal(1, handler.EditCount);
        using var edited = System.Text.Json.JsonDocument.Parse(handler.LastBody);
        Assert.Contains("Новые условия", edited.RootElement.GetProperty("caption").GetString());
        Assert.Equal(oyinQ.Bot.Data.Entities.GatheringPublicationStatus.Published, saved.PublicationStatus);
    }

    [PostgreSqlFact]
    public async Task ConcurrentCreationOperationCommitsOneGatheringAndReplaysWithoutBgg()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        await using var firstDb = database.Open(); await using var secondDb = database.Open();
        var community = (await firstDb.OyinQCommunities.SingleAsync()).ToBotCommunity();
        var command = Command() with { OperationId = Guid.NewGuid() };
        var results = await Task.WhenAll(
            Management(firstDb, new Bgg()).CreateAsync(community, new(actor.TelegramUserId), command, default),
            Management(secondDb, new Bgg()).CreateAsync(community, new(actor.TelegramUserId), command, default));
        Assert.Equal(results[0].PublicId, results[1].PublicId);
        await using var verify = database.Open(); Assert.Equal(1, await verify.GameGatherings.CountAsync());
        var replay = await Management(verify, new Bgg(() => throw new InvalidOperationException("BGG must not be called")))
            .CreateAsync(community, new(actor.TelegramUserId), command with { ConfirmScheduleConflict = false }, default);
        Assert.Equal(results[0].PublicId, replay.PublicId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Management(verify, new Bgg()).CreateAsync(community,
            new(actor.TelegramUserId), command with { MaximumPlayers = 3 }, default));
        Assert.Equal(1, await verify.GameGatherings.CountAsync());
    }

    private sealed class PublicationSender(TaskCompletionSource entered, TaskCompletionSource resume) : ITelegramGroupMessageSender
    {
        public int Calls;
        public async Task<Message> SendMessageAsync(string key, string text, ParseMode mode, ReplyMarkup? markup, CancellationToken ct)
        { Interlocked.Increment(ref Calls); entered.TrySetResult(); await resume.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); return new Message { Id = 123, Chat = new Chat { Id = -10012345 } }; }
        public Task<Message> SendPhotoAsync(string key, InputFile photo, string caption, ParseMode mode, ReplyMarkup? markup, CancellationToken ct) => SendMessageAsync(key, caption, mode, markup, ct);
    }

    private sealed class PublicationBotHandler : HttpMessageHandler
    {
        public int EditCount; public string LastBody = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var isBot = request.RequestUri!.AbsolutePath.EndsWith("getMe");
            if (!isBot) { EditCount++; LastBody = await request.Content!.ReadAsStringAsync(ct); }
            return new(HttpStatusCode.OK) { Content = new StringContent(isBot
                ? "{\"ok\":true,\"result\":{\"id\":123456,\"is_bot\":true,\"first_name\":\"OyinQ\",\"username\":\"test_bot\"}}"
                : "{\"ok\":true,\"result\":{\"message_id\":123,\"date\":0,\"chat\":{\"id\":-10012345,\"type\":\"supergroup\"}}}", Encoding.UTF8, "application/json") };
        }
    }
}
