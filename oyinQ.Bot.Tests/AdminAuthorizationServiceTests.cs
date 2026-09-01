using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;

namespace oyinQ.Bot.Tests;

public sealed class AdminAuthorizationServiceTests
{
    [Fact]
    public async Task SuperAdmin_CanAdministerEveryKnownGroup()
    {
        await using var fixture = CreateFixture(superAdmins: new HashSet<long> { 1 });
        Assert.True(await fixture.Service.CanAdministerCommunityAsync(1, "club-a", default));
        Assert.True(await fixture.Service.CanAdministerCommunityAsync(1, "club-b", default));
        Assert.Empty(fixture.Telegram.VerificationCalls);
    }

    [Fact]
    public async Task SuperAdmin_ListsConfiguredAndObservedGroupsWithoutMembershipChecks()
    {
        await using var fixture = CreateFixture(superAdmins: new HashSet<long> { 1 });
        fixture.Db.KnownTelegramChats.Add(new KnownTelegramChat
        {
            TelegramChatId = -2000, Title = "Новая группа", IsBotPresent = true,
            FirstSeenAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var chats = await fixture.Service.GetAdminPanelChatsAsync(1, default);

        Assert.Equal(3, chats.Count);
        Assert.Contains(chats, x => x.TelegramChatId == -2000 && x.CommunityKey is null);
        Assert.Empty(fixture.Telegram.VerificationCalls);
    }

    [Fact]
    public async Task ApprovedGroupAdmin_CanAdministerOnlyOwnGroup()
    {
        await using var fixture = CreateFixture();
        fixture.Telegram.Allow("club-a", 10);
        await fixture.GrantDirectAsync("club-a", 10);

        Assert.True(await fixture.Service.CanAdministerCommunityAsync(10, "club-a", default));
        Assert.False(await fixture.Service.CanAdministerCommunityAsync(10, "club-b", default));
    }

    [Fact]
    public async Task TelegramAdminWithoutPermission_SeesLockedEntryButCannotAdminister()
    {
        await using var fixture = CreateFixture();
        fixture.Telegram.Allow("club-a", 10);

        var chats = await fixture.Service.GetAdminPanelChatsAsync(10, default);

        Assert.Collection(chats, chat =>
        {
            Assert.Equal("club-a", chat.CommunityKey);
            Assert.False(chat.IsApproved);
        });
        Assert.False(await fixture.Service.CanAdministerCommunityAsync(10, "club-a", default));
    }

    [Fact]
    public async Task TelegramAdmin_CanDiscoverObservedUnconfiguredBotChatAsLocked()
    {
        await using var fixture = CreateFixture();
        fixture.Db.KnownTelegramChats.Add(new KnownTelegramChat
        {
            TelegramChatId = -2000, Title = "Новая группа", IsBotPresent = true,
            FirstSeenAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Telegram.AllowChat(-2000, 10);

        var chats = await fixture.Service.GetAdminPanelChatsAsync(10, default);

        var chat = Assert.Single(chats);
        Assert.Null(chat.CommunityKey);
        Assert.Equal("Новая группа", chat.Name);
        Assert.False(chat.IsApproved);
    }

    [Fact]
    public async Task ApprovedAdmin_LosesEffectiveAccessWhenTelegramRoleIsRemoved()
    {
        await using var fixture = CreateFixture();
        fixture.Telegram.Allow("club-a", 10);
        await fixture.GrantDirectAsync("club-a", 10);
        fixture.Telegram.Deny("club-a", 10);

        Assert.False(await fixture.Service.CanAdministerCommunityAsync(10, "club-a", default));
    }

    [Fact]
    public async Task GroupAdmin_CannotGrantForAnotherGroupOrGrantNonTelegramAdmin()
    {
        await using var fixture = CreateFixture();
        fixture.Telegram.Allow("club-a", 10);
        fixture.Telegram.Allow("club-a", 20);
        await fixture.GrantDirectAsync("club-a", 10);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GrantGroupAdminAsync(
            10, "club-b", 20, null, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GrantGroupAdminAsync(
            10, "club-a", 30, null, null, default));
        Assert.False(fixture.Service.IsSuperAdmin(20));
    }

    [Fact]
    public async Task RevocationImmediatelyBlocksOldOrCraftedTargets()
    {
        await using var fixture = CreateFixture();
        fixture.Telegram.Allow("club-a", 10);
        await fixture.GrantDirectAsync("club-a", 10);

        await fixture.Service.RevokeGroupAdminAsync(10, "club-a", 10, default);

        Assert.False(await fixture.Service.CanAdministerCommunityAsync(10, "club-a", default));
        Assert.False(await fixture.Service.CanAdministerCommunityAsync(10, "club-b", default));
    }

    [Fact]
    public async Task CraftedClubRouteId_ForAnotherGroupIsRejected()
    {
        await using var fixture = CreateFixture();
        fixture.Telegram.Allow("club-a", 10);
        await fixture.GrantDirectAsync("club-a", 10);
        var ownClubId = fixture.Db.Clubs.Single(x => x.BotChatKey == "club-a").Id;
        var otherClubId = fixture.Db.Clubs.Single(x => x.BotChatKey == "club-b").Id;

        Assert.True(await fixture.Service.CanAdministerClubAsync(10, ownClubId, default));
        Assert.False(await fixture.Service.CanAdministerClubAsync(10, otherClubId, default));
    }

    [Fact]
    public async Task UserWithMultiplePermissions_SeesOnlyTelegramAdminGroups()
    {
        await using var fixture = CreateFixture();
        fixture.Telegram.Allow("club-a", 10);
        await fixture.GrantDirectAsync("club-a", 10);
        await fixture.GrantDirectAsync("club-b", 10);

        var chats = await fixture.Service.GetAdminPanelChatsAsync(10, default);

        Assert.Equal(["club-a"], chats.Select(x => x.CommunityKey));
    }

    [Fact]
    public async Task SuperAdmin_CanGrantAndRevokeGroupAdmin()
    {
        await using var fixture = CreateFixture(superAdmins: new HashSet<long> { 1 });
        fixture.Telegram.Allow("club-a", 20);

        await fixture.Service.GrantGroupAdminAsync(1, "club-a", 20, "Bob", "bob", default);
        Assert.True(await fixture.Service.CanAdministerCommunityAsync(20, "club-a", default));
        await fixture.Service.RevokeGroupAdminAsync(1, "club-a", 20, default);
        Assert.False(await fixture.Service.CanAdministerCommunityAsync(20, "club-a", default));
    }

    [Fact]
    public async Task SuperAdmin_CanApproveEligibleAdminWithoutBeingMemberOfTargetChat()
    {
        await using var fixture = CreateFixture(superAdmins: new HashSet<long> { 1 });
        fixture.Telegram.Allow("club-a", 20, "Alice", "alice");

        var candidates = await fixture.Service.ListEligibleGroupAdminsAsync(1, "club-a", default);
        Assert.Equal(20, Assert.Single(candidates).TelegramUserId);
        await fixture.Service.GrantEligibleGroupAdminAsync(1, "club-a", 20, default);

        Assert.True(await fixture.Service.CanAdministerCommunityAsync(20, "club-a", default));
        Assert.False(await fixture.Service.CanAdministerCommunityAsync(20, "club-b", default));
        Assert.DoesNotContain(fixture.Telegram.VerificationCalls, x => x.UserId == 1);
    }

    [Fact]
    public async Task SuperAdmin_CannotApproveUserWhoIsNotCurrentTelegramAdmin()
    {
        await using var fixture = CreateFixture(superAdmins: new HashSet<long> { 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.GrantEligibleGroupAdminAsync(1, "club-a", 20, default));
    }

    [Fact]
    public async Task RemovedBotChat_IsNotListedAsActiveAdminTarget()
    {
        await using var fixture = CreateFixture(superAdmins: new HashSet<long> { 1 });
        fixture.Db.KnownTelegramChats.Add(new KnownTelegramChat
        {
            TelegramChatId = -1001, Title = "Removed", IsBotPresent = false,
            FirstSeenAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var chats = await fixture.Service.GetAdminPanelChatsAsync(1, default);

        Assert.DoesNotContain(chats, x => x.TelegramChatId == -1001);
        Assert.Contains(chats, x => x.TelegramChatId == -1002);
    }

    [Fact]
    public async Task ScopedExport_DoesNotContainAnotherCommunity()
    {
        await using var fixture = CreateFixture();
        fixture.Telegram.Allow("club-a", 10);
        await fixture.GrantDirectAsync("club-a", 10);
        var export = new CsvExportService(fixture.Db, fixture.Service);

        var files = await export.CreateAllAsync(10, "club-a", default);
        var communities = files.Single(x => x.FileName == "communities.csv");
        using var reader = new StreamReader(communities.Content, Encoding.UTF8);
        var csv = await reader.ReadToEndAsync();

        Assert.Contains("club-a", csv);
        Assert.DoesNotContain("club-b", csv);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            export.CreateAllAsync(10, "club-b", default));
    }

    private static Fixture CreateFixture(IReadOnlySet<long>? superAdmins = null) =>
        new(superAdmins ?? new HashSet<long>());

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(IReadOnlySet<long> superAdmins)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new AppDbContext(options);
            Db.OyinQCommunities.AddRange(Community("club-a", -1001), Community("club-b", -1002));
            Db.SaveChanges();
            Db.Clubs.AddRange(Club("club-a"), Club("club-b"));
            Db.SaveChanges();
            Telegram = new FakeTelegramVerifier(Db);
            Service = new AdminAuthorizationService(Db, Telegram,
                Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = superAdmins }),
                TimeProvider.System);
        }

        public AppDbContext Db { get; }
        public FakeTelegramVerifier Telegram { get; }
        public AdminAuthorizationService Service { get; }

        public async Task GrantDirectAsync(string key, long userId)
        {
            Db.ChatAdminPermissions.Add(new ChatAdminPermission
            {
                CommunityKey = key, TelegramUserId = userId, GrantedByTelegramUserId = 1,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await Db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static OyinQCommunity Community(string key, long chatId) => new()
        {
            Key = key, Name = key, TelegramChatId = chatId, Mode = BotMode.Club,
            TimeZoneId = "Asia/Qyzylorda", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };

        private static Club Club(string key) => new()
        {
            BotChatKey = key, Name = key,
            CollectionJson = "{\"version\":2,\"games\":[]}", CollectionRevision = 1,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class FakeTelegramVerifier(AppDbContext db) : ITelegramChatAdministratorVerifier
    {
        private readonly HashSet<(long ChatId, long UserId)> _admins = [];
        private readonly Dictionary<(long ChatId, long UserId), EligibleGroupAdministrator> _profiles = [];

        public List<(long ChatId, long UserId)> VerificationCalls { get; } = [];

        public void Allow(string key, long userId, string? name = null, string? username = null) =>
            AllowChat(ChatId(key), userId, name, username);
        public void AllowChat(long chatId, long userId, string? name = null, string? username = null)
        {
            _admins.Add((chatId, userId));
            _profiles[(chatId, userId)] = new(userId, name, username);
        }
        public void Deny(string key, long userId) => _admins.Remove((ChatId(key), userId));
        public Task<bool> IsAdministratorAsync(long telegramChatId, long telegramUserId,
            CancellationToken cancellationToken)
        {
            VerificationCalls.Add((telegramChatId, telegramUserId));
            return Task.FromResult(_admins.Contains((telegramChatId, telegramUserId)));
        }
        public Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(long telegramChatId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EligibleGroupAdministrator>>(
                _profiles.Where(x => x.Key.ChatId == telegramChatId).Select(x => x.Value).ToArray());
        private long ChatId(string key) => db.OyinQCommunities.Single(x => x.Key == key).TelegramChatId;
    }
}
