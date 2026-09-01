using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Tests;

public sealed class CommunityDeletionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuperAdminDeletion_HidesBindingRevokesConfigurationAndPreservesHistory(bool campMode)
    {
        await using var fixture = new Fixture();
        var community = fixture.AddCommunity(campMode);
        var participant = new Participant
        {
            TelegramUserId = 500, DisplayName = "Игрок", CreatedAt = Now, UpdatedAt = Now
        };
        fixture.Db.Participants.Add(participant);
        fixture.Db.ChatAdminPermissions.Add(new ChatAdminPermission
        {
            Community = community, CommunityKey = community.Key, TelegramUserId = 700,
            GrantedByTelegramUserId = 1, CreatedAt = Now
        });
        fixture.Db.GameGatherings.AddRange(
            Gathering(community, participant, Now.AddDays(-1), GatheringStatus.Completed),
            Gathering(community, participant, Now.AddDays(1), GatheringStatus.Recruiting));
        await fixture.Db.SaveChangesAsync();

        var result = campMode
            ? await fixture.Service.DeleteCampAsync(Fixture.SuperAdminId, community.Camp!.Id, default)
            : await fixture.Service.DeleteClubAsync(Fixture.SuperAdminId, community.Club!.Id, default);

        Assert.False(result.AlreadyDeleted);
        Assert.Single(result.CancelledGatheringIds);
        Assert.NotNull(community.DeletedAt);
        Assert.False(community.IsActive);
        Assert.Null(community.PostingMessageThreadId);
        Assert.NotNull(fixture.Db.ChatAdminPermissions.Single().RevokedAt);
        Assert.Equal(2, fixture.Db.GameGatherings.Count());
        Assert.Equal(GatheringStatus.Completed, fixture.Db.GameGatherings.Single(x => x.StartsAtUtc < Now).Status);
        Assert.Equal(GatheringStatus.Cancelled, fixture.Db.GameGatherings.Single(x => x.StartsAtUtc > Now).Status);
        Assert.Single(fixture.Db.Participants);
        Assert.False(await fixture.Authorization.CanAdministerCommunityAsync(Fixture.SuperAdminId,
            community.Key, default));

        var repeated = campMode
            ? await fixture.Service.DeleteCampAsync(Fixture.SuperAdminId, community.Camp!.Id, default)
            : await fixture.Service.DeleteClubAsync(Fixture.SuperAdminId, community.Club!.Id, default);
        Assert.True(repeated.AlreadyDeleted);
        Assert.Single(repeated.CancelledGatheringIds);
    }

    [Fact]
    public async Task GroupAdminCannotDeleteEvenWithValidOrForgedTargetId()
    {
        await using var fixture = new Fixture();
        var own = fixture.AddCommunity(false);
        var other = fixture.AddCommunity(false, -1002);
        fixture.Db.ChatAdminPermissions.Add(new ChatAdminPermission
        {
            Community = own, CommunityKey = own.Key, TelegramUserId = 700,
            GrantedByTelegramUserId = Fixture.SuperAdminId, CreatedAt = Now
        });
        fixture.TelegramAdmins.Allow(own.TelegramChatId, 700);
        await fixture.Db.SaveChangesAsync();

        Assert.True(await fixture.Authorization.CanAdministerCommunityAsync(700, own.Key, default));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.DeleteClubAsync(700, own.Club!.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.DeleteClubAsync(700, other.Club!.Id, default));
        Assert.All(fixture.Db.OyinQCommunities, x => Assert.Null(x.DeletedAt));
    }

    [Fact]
    public async Task DeletedTelegramChatCanBeRegisteredAgainWithFreshBinding()
    {
        await using var fixture = new Fixture();
        var deleted = fixture.AddCommunity(false);
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.DeleteClubAsync(Fixture.SuperAdminId, deleted.Club!.Id, default);
        var communities = new ManagedCommunityService(fixture.Db, new AllowChatValidator(),
            new FixedTimeProvider(Now));

        var replacement = await communities.CreateClubAsync(new CreateClubCommand(
            "Новый клуб", deleted.TelegramChatId, "UTC", Fixture.SuperAdminId, false), default);

        Assert.NotEqual(deleted.Key, replacement.BotChatKey);
        Assert.Equal(deleted.TelegramChatId, replacement.BotChat.TelegramChatId);
        Assert.Null(replacement.BotChat.DeletedAt);
        Assert.True(replacement.BotChat.IsActive);
    }

    [Fact]
    public void ParticipantDmExport_IsCompleteAndAlwaysWithinTelegramLimit()
    {
        var participants = Enumerable.Range(1, 250).Select(index => new CampAdminParticipant(
            index, $"Игрок {index}", "Алматы", [new DateOnly(2026, 9, 1)], index % 2 == 0,
            $"player{index}", $"https://t.me/player{index}")).ToArray();

        var messages = CampParticipantAdminService.BuildMessages(new(1, "Большой кэмп", participants));

        Assert.True(messages.Count > 1);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, 3900));
        Assert.Contains("250. Игрок 250", string.Join("\n", messages));
    }

    private static GameGathering Gathering(OyinQCommunity community, Participant participant,
        DateTimeOffset startsAt, GatheringStatus status) => new()
    {
        PublicId = Guid.NewGuid(), Community = community, CommunityKey = community.Key,
        OrganizerParticipant = participant, StartsAtUtc = startsAt,
        GameSnapshotJson = "{}", MinimumPlayers = 1, DesiredPlayers = 2, MaximumPlayers = 3,
        Status = status, PublicationStatus = GatheringPublicationStatus.Published,
        CreatedAt = Now, UpdatedAt = Now
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public const long SuperAdminId = 42;

        public Fixture()
        {
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            TelegramAdmins = new MutableTelegramAdmins();
            Authorization = new AdminAuthorizationService(Db, TelegramAdmins,
                Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = new HashSet<long> { SuperAdminId } }),
                new FixedTimeProvider(Now));
            Service = new CommunityDeletionService(Db, Authorization, new FixedTimeProvider(Now));
        }

        public AppDbContext Db { get; }
        public MutableTelegramAdmins TelegramAdmins { get; }
        public AdminAuthorizationService Authorization { get; }
        public CommunityDeletionService Service { get; }

        public OyinQCommunity AddCommunity(bool camp, long chatId = -1001)
        {
            var community = new OyinQCommunity
            {
                Key = $"{(camp ? "camp" : "club")}-{Math.Abs(chatId)}", Name = "Сообщество",
                TelegramChatId = chatId, Mode = camp ? BotMode.Camp : BotMode.Club,
                TimeZoneId = "UTC", IsActive = true, PostingMessageThreadId = 77,
                CreatedAt = Now, UpdatedAt = Now
            };
            if (camp)
                community.Camp = new Camp
                {
                    BotChat = community, BotChatKey = community.Key, Name = community.Name,
                    Status = CampStatus.Active, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 3),
                    CreatedAt = Now, UpdatedAt = Now
                };
            else
                community.Club = new Club
                {
                    BotChat = community, BotChatKey = community.Key, Name = community.Name,
                    CollectionJson = "{\"version\":2,\"games\":[]}", CreatedAt = Now, UpdatedAt = Now
                };
            Db.OyinQCommunities.Add(community);
            return community;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    public sealed class MutableTelegramAdmins : ITelegramChatAdministratorVerifier
    {
        private readonly HashSet<(long ChatId, long UserId)> allowed = [];
        public void Allow(long chatId, long userId) => allowed.Add((chatId, userId));
        public Task<bool> IsAdministratorAsync(long telegramChatId, long telegramUserId,
            CancellationToken cancellationToken) => Task.FromResult(allowed.Contains((telegramChatId, telegramUserId)));
        public Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(long telegramChatId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EligibleGroupAdministrator>>([]);
    }

    private sealed class AllowChatValidator : IManagedChatValidator
    {
        public Task<ManagedChatValidation> ValidateAsync(long telegramChatId,
            long requestingAdministratorId, bool requireRequestingAdministrator,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ManagedChatValidation(true, "Telegram group", null, null));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
