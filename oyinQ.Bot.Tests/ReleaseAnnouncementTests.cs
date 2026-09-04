using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

namespace oyinQ.Bot.Tests;
public sealed class ReleaseAnnouncementTests
{
    [Fact]
    public async Task PreviewAndUnconfirmedRequestsNeverSend_OnlyManagedActiveTargetsAreEligible()
    {
        await using var f = new Fixture();
        var preview = await f.Service.PreviewAsync(f.Data.Me.TelegramUserId, default);
        Assert.Equal(["a", "b"], preview.Targets.Select(x => x.Key)); Assert.All(preview.Targets, x => Assert.True(x.CanPost));
        Assert.Empty(f.Handler.Sends); Assert.Empty(f.Data.Db.ReleaseAnnouncements);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.QueueAsync(f.Data.Me.TelegramUserId, ReleaseContent.Id, ["a"], false, false, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.QueueAsync(f.Data.Me.TelegramUserId, ReleaseContent.Id, ["unknown"], true, false, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.PreviewAsync(f.Data.Other.TelegramUserId, default));
        Assert.Empty(f.Handler.Sends); Assert.Empty(f.Data.Db.ReleaseAnnouncementDeliveries);
    }

    [Fact]
    public async Task ExplicitQueue_UsesConfiguredTopic_RetryOnlyFailed_AndRestartDoesNotResendDelivered()
    {
        await using var f = new Fixture(); f.Handler.FailChat = -1002;
        await f.Service.QueueAsync(f.Data.Me.TelegramUserId, ReleaseContent.Id, ["a", "b"], true, false, default);
        Assert.Empty(f.Handler.Sends);
        Assert.True(await f.Service.DispatchOneAsync(default)); Assert.True(await f.Service.DispatchOneAsync(default));
        var a = await f.Data.Db.ReleaseAnnouncementDeliveries.SingleAsync(x => x.CommunityKey == "a");
        var b = await f.Data.Db.ReleaseAnnouncementDeliveries.SingleAsync(x => x.CommunityKey == "b");
        Assert.Equal(ReleaseDeliveryState.Delivered, a.State); Assert.Equal(123, a.TelegramMessageId);
        Assert.Equal(ReleaseDeliveryState.Failed, b.State);
        Assert.Contains("\"message_thread_id\":42", f.Handler.Sends[0]);
        Assert.Contains("RuntimeReleaseBot", f.Handler.Sends[0]);
        f.Handler.FailChat = null;
        await f.Service.QueueAsync(f.Data.Me.TelegramUserId, ReleaseContent.Id, ["a", "b"], true, true, default);
        Assert.True(await f.NewService().DispatchOneAsync(default)); Assert.False(await f.NewService().DispatchOneAsync(default));
        Assert.Equal(3, f.Handler.Sends.Count);
        Assert.Equal(ReleaseDeliveryState.Delivered, b.State);
        await f.Service.QueueAsync(f.Data.Me.TelegramUserId, ReleaseContent.Id, ["a", "b"], true, false, default);
        Assert.False(await f.Service.DispatchOneAsync(default)); Assert.Equal(3, f.Handler.Sends.Count);
    }

    [Fact]
    public async Task NetworkUnknown_IsNeverAutomaticallyRetried_AndDeletedTargetIsNotSent()
    {
        await using var f = new Fixture(); f.Handler.NetworkFailure = true;
        await f.Service.QueueAsync(f.Data.Me.TelegramUserId, ReleaseContent.Id, ["a", "b"], true, false, default);
        var b = await f.Data.Db.OyinQCommunities.SingleAsync(x => x.Key == "b"); b.DeletedAt = f.Data.Clock.Now; await f.Data.Db.SaveChangesAsync();
        await f.Service.DispatchOneAsync(default); await f.Service.DispatchOneAsync(default);
        Assert.Equal(ReleaseDeliveryState.DeliveryUnknown, (await f.Data.Db.ReleaseAnnouncementDeliveries.SingleAsync(x => x.CommunityKey == "a")).State);
        Assert.Equal(ReleaseDeliveryState.Failed, (await f.Data.Db.ReleaseAnnouncementDeliveries.SingleAsync(x => x.CommunityKey == "b")).State);
        Assert.Single(f.Handler.Sends);
        await f.Service.QueueAsync(f.Data.Me.TelegramUserId, ReleaseContent.Id, ["a"], true, true, default);
        Assert.False(await f.Service.DispatchOneAsync(default));
    }

    [Fact]
    public async Task PreparationFailureIsRetryable_WithoutPretendingSendWasAttempted()
    {
        await using var f = new Fixture();
        await f.Service.QueueAsync(f.Data.Me.TelegramUserId, ReleaseContent.Id, ["a"], true, false, default);
        f.Handler.PreparationFailure = true;
        await f.Service.DispatchOneAsync(default);
        var row = await f.Data.Db.ReleaseAnnouncementDeliveries.SingleAsync();
        Assert.Equal(ReleaseDeliveryState.Failed, row.State);
        Assert.Empty(f.Handler.Sends);
        f.Handler.PreparationFailure = false;
        Assert.True((await f.Service.PreviewAsync(f.Data.Me.TelegramUserId, default)).Targets.Single(x => x.Key == "a").CanRetry);
        await f.Service.QueueAsync(f.Data.Me.TelegramUserId, ReleaseContent.Id, ["a"], true, true, default);
        await f.NewService().DispatchOneAsync(default);
        Assert.Equal(ReleaseDeliveryState.Delivered, row.State); Assert.Single(f.Handler.Sends);
    }

    [Fact]
    public void ReleaseTextAndDescription_AreBoundedAndProductFacing()
    {
        Assert.InRange(ReleaseContent.Text.Length, 1, 3500);
        Assert.InRange(TelegramBotProfile.Description.Length, 1, 512);
        Assert.InRange(TelegramEntryText.FunctionalityGuide.Length, 1, 4096);
        Assert.Equal("Настольные игры, сборы и коллекции клубов и кэмпов.", TelegramBotProfile.ShortDescription);
        Assert.Contains("личные", TelegramBotProfile.Description); Assert.Contains("сыгранных", TelegramBotProfile.Description);
        Assert.DoesNotContain("Mini App", TelegramBotProfile.Description);
        Assert.DoesNotContain("/oyinq", ReleaseContent.Text);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public PlanningFixture Data { get; } = new();
        public Handler Handler { get; } = new();
        private readonly TelegramBotClient bot;
        public ReleaseAnnouncementService Service => NewService();
        public Fixture()
        {
            bot = new("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", new HttpClient(Handler));
            foreach (var (key, chat) in new[] { ("a", -1001L), ("b", -1002L), ("inactive", -1003L), ("deleted", -1004L) })
                Data.Db.OyinQCommunities.Add(new() { Key = key, Name = key, Mode = BotMode.Club, TelegramChatId = chat, TimeZoneId = "UTC",
                    IsActive = key != "inactive", DeletedAt = key == "deleted" ? Data.Clock.Now : null, PostingMessageThreadId = 42 });
            Data.Db.KnownTelegramChats.Add(new() { TelegramChatId = -1001, IsForum = true, IsBotPresent = true });
            Data.Db.KnownTelegramChats.Add(new() { TelegramChatId = -9999, Title = "Только известный чат", IsBotPresent = true });
            Data.Db.SaveChanges();
        }
        public ReleaseAnnouncementService NewService()
        {
            var options = Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = new HashSet<long> { Data.Me.TelegramUserId } });
            var auth = new AdminAuthorizationService(Data.Db, null!, options, Data.Clock);
            var sender = new TelegramGroupMessageSender(Data.Db, bot, options, new NotificationService(Data.Db, Data.Clock), Data.Clock, NullLogger<TelegramGroupMessageSender>.Instance);
            return new(Data.Db, auth, bot, sender, Data.Clock);
        }
        public ValueTask DisposeAsync() => Data.DisposeAsync();
    }
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Sends { get; } = [];
        public long? FailChat { get; set; }
        public bool NetworkFailure { get; set; }
        public bool PreparationFailure { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var method = request.RequestUri!.AbsolutePath.Split('/').Last().ToLowerInvariant();
            if (PreparationFailure && method == "getme") throw new HttpRequestException("preparation failed");
            var response = method switch
            {
                "getme" => "{\"id\":123456,\"is_bot\":true,\"first_name\":\"OyinQ\",\"username\":\"RuntimeReleaseBot\"}",
                "getchatmember" => "{\"status\":\"creator\",\"user\":{\"id\":123456,\"is_bot\":true,\"first_name\":\"OyinQ\"},\"is_anonymous\":false}",
                "getchat" => "{\"id\":-1001,\"type\":\"supergroup\",\"title\":\"Клуб\",\"accent_color_id\":0,\"max_reaction_count\":1}",
                "sendmessage" => "{\"message_id\":123,\"date\":0,\"chat\":{\"id\":-1001,\"type\":\"supergroup\"}}",
                _ => throw new InvalidOperationException(method)
            };
            if (method == "sendmessage")
            {
                Sends.Add(body);
                if (NetworkFailure) throw new HttpRequestException("network");
                if (FailChat is { } id && body.Contains($"\"chat_id\":{id}"))
                    return new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"ok\":false,\"error_code\":403,\"description\":\"Forbidden\"}", Encoding.UTF8, "application/json") };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":" + response + "}", Encoding.UTF8, "application/json") };
        }
    }
}
