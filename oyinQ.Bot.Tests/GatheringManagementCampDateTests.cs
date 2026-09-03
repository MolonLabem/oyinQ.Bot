using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.MiniApp;

namespace oyinQ.Bot.Tests;

public sealed class GatheringManagementCampDateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CommunityContract_LoadsAuthoritativeCampDateBounds()
    {
        await using var fixture = await Fixture.CreateAsync();

        var community = await new CommunityStore(fixture.Db).FindByKeyAsync("camp", default);

        Assert.Equal(new DateOnly(2026, 9, 10), community!.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 13), community.EndDate);
    }

    [Fact]
    public async Task ForgedCreateOutsideCampRange_IsRejectedBeforeGameSelection()
    {
        await using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CreateAsync(fixture.Community, new TelegramMiniAppIdentity(Fixture.TelegramUserId),
                new CreateGatheringCommand("camp", "catalog", 42, [],
                    new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.FromHours(5)),
                    2, 3, 4, null, true), default));

        Assert.Equal("Дата сбора должна быть в пределах дат кэмпа: 10–13 сентября.", error.Message);
        Assert.Empty(fixture.Db.GameGatherings);
    }

    [Fact]
    public async Task EditingGatheringOutsideCampRange_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var gathering = new GameGathering
        {
            PublicId = Guid.NewGuid(), CommunityKey = "camp",
            OrganizerParticipantId = fixture.Participant.Id,
            StartsAtUtc = new DateTimeOffset(2026, 9, 12, 13, 0, 0, TimeSpan.Zero),
            MinimumPlayers = 2, DesiredPlayers = 3, MaximumPlayers = 4,
            Status = GatheringStatus.Recruiting,
            GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new GatheringGameSnapshot(
                GatheringGameSnapshot.CurrentVersion, 42, "Игра", null, null, 2, 4, null, [],
                KnownExpansions: []))
        };
        fixture.Db.GameGatherings.Add(gathering);
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UpdateAsync(gathering.PublicId, "camp", Fixture.TelegramUserId,
                new UpdateGatheringCommand(
                    new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.FromHours(5)),
                    2, 3, 4, null, true, []), default));

        Assert.Equal("Дата сбора должна быть в пределах дат кэмпа: 10–13 сентября.", error.Message);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 13, 0, 0, TimeSpan.Zero), gathering.StartsAtUtc);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const long TelegramUserId = 700;
        private readonly FixedTimeProvider timeProvider = new(Now);
        public AppDbContext Db { get; }
        public Participant Participant { get; }
        public BotCommunity Community { get; } = new("camp", "Кэмп", -1001, BotMode.Camp,
            "Asia/Qyzylorda", new(2026, 9, 10), new(2026, 9, 13));
        public GatheringManagementService Service { get; }

        private Fixture(AppDbContext db, Participant participant)
        {
            Db = db;
            Participant = participant;
            Service = new GatheringManagementService(db, null!,
                new CampParticipationPolicy(db, timeProvider), null!, timeProvider);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
            var community = new OyinQCommunity
            {
                Key = "camp", Name = "Кэмп", TelegramChatId = -1001, Mode = BotMode.Camp,
                TimeZoneId = "Asia/Qyzylorda", IsActive = true
            };
            var camp = new Camp
            {
                BotChat = community, BotChatKey = community.Key, Name = community.Name,
                Status = CampStatus.Active, StartDate = new(2026, 9, 10), EndDate = new(2026, 9, 13)
            };
            var participant = new Participant
            {
                TelegramUserId = TelegramUserId, DisplayName = "Организатор"
            };
            db.AddRange(community, camp, participant);
            await db.SaveChangesAsync();
            return new Fixture(db, participant);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
