using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Tests;

public sealed class GatheringPublicationReliabilityTests
{
    [Fact]
    public async Task RecoveryDoesNotPublishOldNeverAnnouncedHistory()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddDays(-1));
        g.Status = GatheringStatus.Completed; await f.Db.SaveChangesAsync(); var sender = new Sender();
        Assert.False(await Service(f, sender).PublishAsync(g.PublicId, default));
        Assert.Equal(GatheringPublicationStatus.NotRequired, g.PublicationStatus); Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task PublishedCreationReplayDoesNotPostAgain()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        await f.Db.SaveChangesAsync(); var sender = new Sender(); var service = Service(f, sender);
        Assert.True(await service.PublishAsync(g.PublicId, default));
        Assert.True(await service.PublishAsync(g.PublicId, default));
        Assert.Equal(1, sender.Calls); Assert.Equal(123, g.TelegramMessageId);
        Assert.Equal(GatheringPublicationStatus.Published, g.PublicationStatus);
    }

    [Fact]
    public async Task LostPhotoResponseDoesNotFallBackOrPermitRetryEvenAfterMutation()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        g.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson) with { ImageUrl = "https://example.com/game.png" });
        await f.Db.SaveChangesAsync(); var sender = new Sender { Failure = new HttpRequestException("response lost") };
        var service = Service(f, sender);
        Assert.False(await service.PublishAsync(g.PublicId, default));
        Assert.Equal(GatheringPublicationStatus.DeliveryUnknown, g.PublicationStatus);
        GatheringPublication.Request(g); await f.Db.SaveChangesAsync();
        Assert.False(await service.PublishAsync(g.PublicId, default));
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task DefinitiveRejectionAllowsExplicitRetry()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        await f.Db.SaveChangesAsync(); var sender = new Sender { Failure = new ApiRequestException("Forbidden", 403) };
        var service = Service(f, sender);
        Assert.False(await service.PublishAsync(g.PublicId, default));
        Assert.Equal(GatheringPublicationStatus.Failed, g.PublicationStatus);
        sender.Failure = null;
        Assert.True(await service.PublishAsync(g.PublicId, default)); Assert.Equal(2, sender.Calls);
    }

    [Theory]
    [InlineData(GatheringPublicationStatus.Preparing, false, GatheringPublicationStatus.Failed)]
    [InlineData(GatheringPublicationStatus.Delivering, false, GatheringPublicationStatus.DeliveryUnknown)]
    [InlineData(GatheringPublicationStatus.Delivering, true, GatheringPublicationStatus.Failed)]
    public async Task ExpiredLeaseDistinguishesPreparationNewSendAndEdit(GatheringPublicationStatus before, bool existing, GatheringPublicationStatus expected)
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        g.PublicationStatus = before; g.PublicationAttemptId = Guid.NewGuid(); g.PublicationLeaseExpiresAt = f.Clock.Now.AddMinutes(-1);
        if (existing) { g.TelegramChatId = -1001; g.TelegramMessageId = 123; }
        await f.Db.SaveChangesAsync(); var sender = new Sender();
        Assert.False(await Service(f, sender).PublishAsync(g.PublicId, default));
        Assert.Equal(expected, g.PublicationStatus); Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task PreparationRevisionChangeAbandonsStaleContentsBeforeSend()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        await f.Db.SaveChangesAsync(); var sender = new Sender
        {
            Prepare = async () => { GatheringPublication.Request(g); g.Description = "changed"; await f.Db.SaveChangesAsync(); }
        };
        Assert.False(await Service(f, sender).PublishAsync(g.PublicId, default));
        Assert.Equal(GatheringPublicationStatus.Pending, g.PublicationStatus); Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task CancellationBeforeSendRemainsRetryable()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        await f.Db.SaveChangesAsync(); using var cancelled = new CancellationTokenSource();
        var sender = new Sender { Prepare = () => { cancelled.Cancel(); cancelled.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(f, sender).PublishAsync(g.PublicId, cancelled.Token));
        Assert.Equal(GatheringPublicationStatus.Failed, g.PublicationStatus); Assert.Equal(0, sender.Calls);
    }

    private static GatheringPublicationService Service(PlanningFixture f, Sender sender)
    {
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", new HttpClient(new BotHandler()));
        return new(f.Db, new CommunityStore(f.Db), new(sender, bot, new GatheringPresentationService(), NullLogger<GatheringTelegramPublisher>.Instance),
            f.Clock, NullLogger<GatheringPublicationService>.Instance);
    }

    private sealed class Sender : ITelegramGroupMessageSender
    {
        public int Calls; public Exception? Failure; public Func<Task>? Prepare;
        public async Task<Func<CancellationToken, Task<Message>>> PrepareMessageAsync(string key, string text, ParseMode mode, ReplyMarkup? markup, CancellationToken ct)
        { if (Prepare is not null) await Prepare(); return token => SendMessageAsync(key, text, mode, markup, token); }
        public Task<Message> SendMessageAsync(string key, string text, ParseMode mode, ReplyMarkup? markup, CancellationToken ct)
        { Calls++; return Failure is null ? Task.FromResult(new Message { Id = 123, Chat = new Chat { Id = -1001 } }) : Task.FromException<Message>(Failure); }
        public Task<Message> SendPhotoAsync(string key, InputFile photo, string caption, ParseMode mode, ReplyMarkup? markup, CancellationToken ct) => SendMessageAsync(key, caption, mode, markup, ct);
    }
    private sealed class BotHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"ok\":true,\"result\":{\"id\":123456,\"is_bot\":true,\"first_name\":\"OyinQ\",\"username\":\"test_bot\"}}", Encoding.UTF8, "application/json") });
    }
}
