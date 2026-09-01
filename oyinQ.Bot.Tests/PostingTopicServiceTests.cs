using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Tests;

public sealed class PostingTopicServiceTests
{
    [Fact]
    public async Task NonForumChat_DoesNotExposeTopicSetting()
    {
        await using var fixture = CreateFixture();
        fixture.Authorize(10, "a");

        var settings = await fixture.Service.GetAsync(10, "a", default);

        Assert.False(settings.IsForum);
        Assert.Empty(settings.KnownTopics);
    }

    [Fact]
    public async Task ForumChat_ExposesSettingAndDefaultsToMainTopic()
    {
        await using var fixture = CreateFixture(forumA: true);
        fixture.Authorize(10, "a");

        var settings = await fixture.Service.GetAsync(10, "a", default);

        Assert.True(settings.IsForum);
        Assert.Null(settings.MessageThreadId);
        Assert.False(settings.NeedsSelection);
    }

    [Fact]
    public async Task ApprovedGroupAdmin_SelectsKnownTopicForOwnGroupOnly()
    {
        await using var fixture = CreateFixture(forumA: true, forumB: true);
        fixture.Authorize(10, "a");
        fixture.AddTopic(-1001, 42, "Сборы");
        fixture.AddTopic(-1002, 42, "Другие сборы");
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.SetAsync(10, "a", 42, default);

        Assert.Equal(42, fixture.Db.OyinQCommunities.Single(x => x.Key == "a").PostingMessageThreadId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.SetAsync(10, "b", 42, default));
    }

    [Fact]
    public async Task LockedTelegramAdmin_CannotChangeTopic()
    {
        await using var fixture = CreateFixture(forumA: true);
        fixture.AddTopic(-1001, 42, "Сборы");
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.SetAsync(10, "a", 42, default));
    }

    [Fact]
    public async Task SuperAdmin_CanConfigureAnyGroup()
    {
        await using var fixture = CreateFixture(forumA: true, forumB: true, superAdmin: 1);
        fixture.AddTopic(-1001, 42, "A");
        fixture.AddTopic(-1002, 43, "B");
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.SetAsync(1, "a", 42, default);
        await fixture.Service.SetAsync(1, "b", 43, default);

        Assert.Equal(42, fixture.Db.OyinQCommunities.Single(x => x.Key == "a").PostingMessageThreadId);
        Assert.Equal(43, fixture.Db.OyinQCommunities.Single(x => x.Key == "b").PostingMessageThreadId);
    }

    [Fact]
    public async Task ForgedTopicFromAnotherChat_IsRejectedEvenWhenThreadIdsDiffer()
    {
        await using var fixture = CreateFixture(forumA: true, forumB: true);
        fixture.Authorize(10, "a");
        fixture.AddTopic(-1002, 99, "Чужая тема");
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.SetAsync(10, "a", 99, default));
    }

    [Fact]
    public async Task SameThreadId_InDifferentChatsDoesNotCollide()
    {
        await using var fixture = CreateFixture(forumA: true, forumB: true, superAdmin: 1);
        fixture.AddTopic(-1001, 42, "A");
        fixture.AddTopic(-1002, 42, "B");
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.SetAsync(1, "a", 42, default);
        await fixture.Service.SetAsync(1, "b", 42, default);

        Assert.Equal(2, fixture.Db.TelegramForumTopics.Count(x => x.MessageThreadId == 42));
    }

    [Fact]
    public async Task IncomingTopicMessage_IsDiscoveredForItsChat()
    {
        await using var fixture = CreateFixture(forumA: true);

        await fixture.Service.ObserveAsync(Message(-1001, 77), default);

        var topic = Assert.Single(fixture.Db.TelegramForumTopics);
        Assert.Equal(-1001, topic.TelegramChatId);
        Assert.Equal(77, topic.MessageThreadId);
    }

    [Fact]
    public async Task RenameUpdatesNameWithoutChangingConfiguredIdentity()
    {
        await using var fixture = CreateFixture(forumA: true);
        fixture.Authorize(10, "a");
        fixture.AddTopic(-1001, 42, "Сборы");
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.SetAsync(10, "a", 42, default);
        var renamed = Message(-1001, 42);
        renamed.ForumTopicEdited = new ForumTopicEdited { Name = "Игровые сборы" };

        await fixture.Service.ObserveAsync(renamed, default);

        Assert.Equal(42, fixture.Db.OyinQCommunities.Single(x => x.Key == "a").PostingMessageThreadId);
        Assert.Equal("Игровые сборы", fixture.Db.TelegramForumTopics.Single().Name);
    }

    [Fact]
    public async Task ClosedConfiguredTopic_IsInvalidatedImmediately()
    {
        await using var fixture = CreateFixture(forumA: true);
        fixture.Authorize(10, "a");
        fixture.AddTopic(-1001, 42, "Сборы");
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.SetAsync(10, "a", 42, default);
        var closed = Message(-1001, 42);
        closed.ForumTopicClosed = new ForumTopicClosed();

        await fixture.Service.ObserveAsync(closed, default);

        var community = fixture.Db.OyinQCommunities.Single(x => x.Key == "a");
        Assert.Null(community.PostingMessageThreadId);
        Assert.NotNull(community.PostingTopicInvalidatedAt);
        Assert.True(fixture.Db.TelegramForumTopics.Single().IsClosed);
    }

    [Fact]
    public async Task TelegramSideSelectionRequiresActualTopicAndExactChatAuthorization()
    {
        await using var fixture = CreateFixture(forumA: true, forumB: true);
        fixture.Authorize(10, "a");

        await fixture.Service.SelectFromTelegramAsync(10, Message(-1001, 55), default);

        Assert.Equal(55, fixture.Db.OyinQCommunities.Single(x => x.Key == "a").PostingMessageThreadId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.SelectFromTelegramAsync(10, Message(-1002, 55), default));
    }

    private static Fixture CreateFixture(bool forumA = false, bool forumB = false, long? superAdmin = null) =>
        new(forumA, forumB, superAdmin);

    private static Message Message(long chatId, int threadId) => new()
    {
        Id = threadId + 100,
        Date = DateTime.UtcNow,
        MessageThreadId = threadId,
        Chat = new Chat { Id = chatId, Type = ChatType.Supergroup, IsForum = true }
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(bool forumA, bool forumB, long? superAdmin)
        {
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Db.OyinQCommunities.AddRange(Community("a", -1001), Community("b", -1002));
            Db.KnownTelegramChats.AddRange(Known(-1001, forumA), Known(-1002, forumB));
            Db.SaveChanges();
            Authorization = new StubAuthorization(superAdmin);
            Service = new PostingTopicService(Db, Authorization, new CachedForumCapability(), TimeProvider.System);
        }

        public AppDbContext Db { get; }
        public StubAuthorization Authorization { get; }
        public PostingTopicService Service { get; }
        public void Authorize(long userId, string key) => Authorization.Allowed.Add((userId, key));
        public void AddTopic(long chatId, int threadId, string name) => Db.TelegramForumTopics.Add(new()
        {
            TelegramChatId = chatId, MessageThreadId = threadId, Name = name,
            LastSeenAt = DateTimeOffset.UtcNow
        });
        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static OyinQCommunity Community(string key, long chatId) => new()
        {
            Key = key, Name = key, TelegramChatId = chatId, Mode = BotMode.Club,
            TimeZoneId = "UTC", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        private static KnownTelegramChat Known(long chatId, bool forum) => new()
        {
            TelegramChatId = chatId, IsForum = forum, FirstSeenAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class CachedForumCapability : ITelegramChatForumCapabilityResolver
    {
        public Task<bool?> GetIsForumAsync(long telegramChatId, CancellationToken cancellationToken) =>
            Task.FromResult<bool?>(null);
    }

    private sealed class StubAuthorization(long? superAdmin) : IAdminAuthorizationService
    {
        public HashSet<(long UserId, string Key)> Allowed { get; } = [];
        public bool IsSuperAdmin(long telegramUserId) => telegramUserId == superAdmin;
        public Task<bool> CanAdministerCommunityAsync(long userId, string key, CancellationToken token) =>
            Task.FromResult(IsSuperAdmin(userId) || Allowed.Contains((userId, key)));
        public Task<bool> CanOpenAdminPanelAsync(long userId, CancellationToken token) => Task.FromResult(true);
        public Task<bool> CanAdministerClubAsync(long userId, long id, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> CanAdministerCampAsync(long userId, long id, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> CanManageAdminsAsync(long userId, string key, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdminChatAccess>> GetAdminPanelChatsAsync(long userId, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<GroupAdministratorRecord>> ListGroupAdminsAsync(long userId, string key, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<EligibleGroupAdministrator>> ListEligibleGroupAdminsAsync(long userId, string key, CancellationToken token) => throw new NotSupportedException();
        public Task GrantEligibleGroupAdminAsync(long actor, string key, long target, CancellationToken token) => throw new NotSupportedException();
        public Task GrantGroupAdminAsync(long actor, string key, long target, string? name, string? username, CancellationToken token) => throw new NotSupportedException();
        public Task RevokeGroupAdminAsync(long actor, string key, long target, CancellationToken token) => throw new NotSupportedException();
    }
}
