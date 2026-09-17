using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Tests;

public sealed class CatalogCollectionDetailsTests
{
    [Fact]
    public async Task Details_UsesCommunityAndBggIdentity_NotLocalizedTitle()
    {
        await using var fixture = Fixture.Create();
        fixture.AddClub("club-a", Game(167791, "Покорение Марса",
            [new ClubCollectionExpansion(1, "Prelude")]));
        fixture.AddClub("club-b", Game(42, "Другая игра"));
        await fixture.Db.SaveChangesAsync();

        var details = await fixture.Service.DetailsAsync("club-a", BotMode.Club, 100,
            167791, default);

        Assert.Equal(167791, details.BggId);
        Assert.Equal("Покорение Марса", details.Name);
        Assert.Equal("Prelude", Assert.Single(details.Expansions).Name);
        Assert.Equal(0, details.ScheduledGatherings);
        Assert.Equal(0, details.RecordedPlays);
        var absent = await Assert.ThrowsAsync<GameNotInCollectionException>(() =>
            fixture.Service.DetailsAsync("club-b", BotMode.Club, 100, 167791, default));
        Assert.Equal(167791, absent.BggId);
    }

    [Fact]
    public async Task Details_DoesNotInventUnavailableExpansions()
    {
        await using var fixture = Fixture.Create();
        fixture.AddClub("club", Game(99, "Игра без дополнений"));
        await fixture.Db.SaveChangesAsync();

        var details = await fixture.Service.DetailsAsync("club", BotMode.Club, 100, 99, default);

        Assert.Empty(details.Expansions);
    }

    [Fact]
    public async Task CampDetails_UsesEffectiveBaseContributionsProvidersAndAvailableExpansions()
    {
        await using var fixture = Fixture.Create();
        var camp = fixture.AddCamp("camp", Game(10, "Brass"));
        var participant = new Participant { TelegramUserId = 100, DisplayName = "Игрок" };
        fixture.Db.CampRegistrations.Add(new CampRegistration { Camp = camp, Participant = participant,
            City = "Алматы", NeedsAccommodation = false, SelectedDays = [new() { Date = new DateOnly(2026, 9, 4) }] });
        camp.Contributions.Add(new CampGameContribution
        {
            Participant = participant, BggId = 10, ItemType = CollectionItemType.BaseGame,
            Source = CollectionItemSource.Manual, Commitment = CampBringCommitment.Bringing,
            SnapshotJson = CollectionItemSnapshotSerializer.Serialize(Snapshot("Brass"))
        });
        camp.Contributions.Add(new CampGameContribution
        {
            Participant = participant, BggId = 30, ItemType = CollectionItemType.Expansion,
            Source = CollectionItemSource.Manual, Commitment = CampBringCommitment.Available,
            ParentBggId = 10,
            SnapshotJson = CollectionItemSnapshotSerializer.Serialize(Snapshot("Brass: Birmingham", [10]))
        });
        await fixture.Db.SaveChangesAsync();

        var details = await fixture.Service.DetailsAsync("camp", BotMode.Camp, 100, 10, default);

        Assert.True(details.Availability.IsInBaseCollection);
        Assert.True(details.Availability.HasCommittedProvider);
        Assert.Equal("Игрок", Assert.Single(details.Availability.Providers).DisplayName);
        Assert.Equal(30, Assert.Single(details.Expansions).BggId);
    }

    [Fact]
    public async Task SharedSourceChangesReachTwoClubsAndTwoCamps_WithoutChangingLocalData()
    {
        await using var fixture = Fixture.Create();
        var source = fixture.AddClub("source", Game(10, "Старая игра"));
        var linked = fixture.AddClub("linked", Game(999, "Старая копия"));
        var linked2 = fixture.AddClub("linked2");
        var unrelated = fixture.AddClub("unrelated", Game(77, "Отдельная коллекция"));
        var camp = fixture.AddCamp("camp", Game(999, "Старый снимок"));
        var camp2 = fixture.AddCamp("camp2");
        await fixture.Db.SaveChangesAsync();
        linked.SourceClubId = source.Id; linked2.SourceClubId = linked.Id;
        camp.SourceClubId = source.Id; camp2.SourceClubId = linked2.Id;
        var participant = new Participant { TelegramUserId = 100, DisplayName = "Игрок" };
        fixture.Db.CampRegistrations.Add(new CampRegistration { Camp = camp, Participant = participant,
            City = "Алматы", NeedsAccommodation = false, SelectedDays = [new() { Date = new DateOnly(2026, 9, 4) }] });
        var contribution = new CampGameContribution { Camp = camp, Participant = participant,
            BggId = 10, ItemType = CollectionItemType.BaseGame, Commitment = CampBringCommitment.Bringing,
            SnapshotJson = CollectionItemSnapshotSerializer.Serialize(Snapshot("Личная коробка")) };
        fixture.Db.Add(contribution);
        fixture.Db.ParticipantCollectionItems.Add(new ParticipantCollectionItem { Participant = participant,
            BggId = 10, ItemType = CollectionItemType.BaseGame,
            SnapshotJson = CollectionItemSnapshotSerializer.Serialize(Snapshot("Личная коробка")) });
        var gatheringSnapshot = Features.Gatherings.GatheringGameSnapshotSerializer.Serialize(new(
            Features.Gatherings.GatheringGameSnapshot.CurrentVersion, 10, "Историческая игра", null, null, 1, 4, null, [], "collection", []));
        fixture.Db.GameGatherings.Add(new GameGathering { PublicId = Guid.NewGuid(), CommunityKey = "camp",
            GameSnapshotJson = gatheringSnapshot, OrganizerParticipant = participant,
            StartsAtUtc = DateTimeOffset.UtcNow.AddDays(-1), Status = GatheringStatus.Completed });
        fixture.Db.GameWishes.Add(new GameWish { CommunityKey = "camp", Participant = participant, BggId = 10,
            SnapshotJson = ClubCollectionSerializer.Serialize(new(2, [Game(10, "Желаемая игра")])) });
        await fixture.Db.SaveChangesAsync();
        var snapshot = camp.BaseCollectionJson;
        var query = new CatalogQuery(null, null, [], [], "name");
        foreach (var key in new[] { "linked", "linked2", "camp", "camp2" })
        {
            var mode = key.StartsWith("camp") ? BotMode.Camp : BotMode.Club;
            Assert.Contains(await fixture.Service.LoadAsync(key, mode, 100, default), x => x.Game.BggId == 10 && x.IsInBaseCollection);
        }
        for (var repeat = 0; repeat < 2; repeat++)
        {
            source.ReplaceCollection(new(ClubCollectionDocument.CurrentVersion, [Game(20, "Новая игра") with {
                CategoryItems = [new(1001, "Economic")], Type = GameType.Strategy }]), DateTimeOffset.UtcNow);
            await fixture.Db.SaveChangesAsync();
            foreach (var key in new[] { "linked", "linked2", "camp", "camp2" })
            {
                var mode = key.StartsWith("camp") ? BotMode.Camp : BotMode.Club;
                var result = await fixture.Service.ListAsync(key, mode, 100, query with { Ownership = "club" }, default);
                Assert.Equal(20, Assert.Single(result.Items).BggId);
                Assert.Equal(1, result.Total);
                Assert.Equal(1001, Assert.Single(result.Filters.Categories).BggId);
                Assert.Equal(result.Total, (await fixture.Service.ListAsync(key, mode, 100, query with { Ownership = "club" }, default, true)).Total);
                if (mode == BotMode.Camp) Assert.False(result.Items[0].IsDefinitelyAvailable);
            }
        }
        Assert.Equal(snapshot, camp.BaseCollectionJson);
        Assert.Single(await fixture.Db.GameWishes.ToArrayAsync());
        Assert.Equal(gatheringSnapshot, (await fixture.Db.GameGatherings.SingleAsync()).GameSnapshotJson);
        Assert.Equal(CampBringCommitment.Bringing, (await fixture.Db.CampGameContributions.SingleAsync()).Commitment);
        Assert.Single(await fixture.Db.ParticipantCollectionItems.ToArrayAsync());
        Assert.Equal(77, Assert.Single(unrelated.ReadCollection().Games).BggId);
        var retained = await fixture.Service.LoadAsync("camp", BotMode.Camp, 100, default);
        Assert.Contains(retained, x => x.Game.BggId == 10 && !x.IsInBaseCollection && x.Providers.Count == 1);
        Assert.Throws<InvalidOperationException>(() => linked.ReplaceCollection(ClubCollectionDocument.Empty, DateTimeOffset.UtcNow));
        Assert.False((await new ClubCollectionService(fixture.Db).GetAsync(linked.Id, default)).CanEdit);
    }

    [Fact]
    public async Task ClubIgnoresCampConstraints_CountUsesSameExpandedPlayerRangeAndGrouping()
    {
        await using var fixture = Fixture.Create();
        fixture.AddClub("club", Game(10, "Игра", [new(20, "Дополнение", MinPlayers: 2, MaxPlayers: 6)]));
        await fixture.Db.SaveChangesAsync();
        var query = new CatalogQuery(null, 6, [], [], "popular", "participants", "possible", ProviderParticipantIds: [999]);
        var list = await fixture.Service.ListAsync("club", BotMode.Club, 100, query, default);
        Assert.Single(list.Items);
        var preview = await fixture.Service.ListAsync("club", BotMode.Club, 100, query, default, true);
        Assert.Equal(list.Total, preview.Total); Assert.Empty(preview.Items);
        Assert.Equal(0, (await fixture.Service.ListAsync("club", BotMode.Club, 100, query with { Players = 7 }, default, true)).Total);
    }

    [Fact]
    public async Task CampAvailabilityRequiresExplicitCommitment_AndLinksRejectCycles()
    {
        await using var fixture = Fixture.Create();
        var source = fixture.AddClub("source", Game(10, "Игра"));
        var camp = fixture.AddCamp("camp"); await fixture.Db.SaveChangesAsync();
        camp.SourceClubId = source.Id; await fixture.Db.SaveChangesAsync();
        var query = new CatalogQuery(null, null, [], [], null, Availability: "confirmed");
        Assert.Empty((await fixture.Service.ListAsync("camp", BotMode.Camp, 100, query, default)).Items);
        Assert.Single((await fixture.Service.ListAsync("camp", BotMode.Camp, 100, query with { Availability = null }, default)).Items);
        source.SourceClubId = source.Id; await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SharedCollectionReader(fixture.Db).SourceAsync(source.Id, default));
    }

    [Theory]
    [InlineData(BotMode.Club)]
    [InlineData(BotMode.Camp)]
    public async Task YearAndBestPlayers_ListAndPreviewUseTheSameStoredMetadata(BotMode mode)
    {
        await using var fixture = Fixture.Create();
        ClubCollectionGame[] games = [
            Game(10, "A") with { YearPublished = 2014, BestPlayers = "2–4" },
            Game(20, "B") with { YearPublished = 2015, BestPlayers = "2–4, 6" },
            Game(30, "C") with { YearPublished = 2020, BestPlayers = "4" },
            Game(40, "D") with { YearPublished = 2021, BestPlayers = null },
            Game(50, "E") with { YearPublished = null, BestPlayers = "4" }
        ];
        if (mode == BotMode.Club) fixture.AddClub("catalog", games);
        else fixture.AddCamp("catalog", games);
        await fixture.Db.SaveChangesAsync();
        var all = new CatalogQuery(null, null, [], [], "name");
        (CatalogQuery Query, long[] Ids)[] cases = [
            (all, [10, 20, 30, 40, 50]),
            (all with { FromYear = 2015 }, [20, 30, 40]),
            (all with { ToYear = 2015 }, [10, 20]),
            (all with { FromYear = 2015, ToYear = 2020 }, [20, 30]),
            (all with { FromYear = 2015, ToYear = 2015 }, [20]),
            (all with { Players = 4, PlayerCountMode = CatalogPlayerCountMode.Best }, [10, 20, 30, 50]),
            (all with { Players = 4, FromYear = 2015, ToYear = 2021, PlayerCountMode = CatalogPlayerCountMode.Best }, [20, 30]),
            (all with { Players = 6, PlayerCountMode = CatalogPlayerCountMode.Best }, [20]),
            (all with { Players = 5, PlayerCountMode = CatalogPlayerCountMode.Best }, [])
        ];
        foreach (var (query, ids) in cases)
        {
            var list = await fixture.Service.ListAsync("catalog", mode, 100, query, default);
            var preview = await fixture.Service.ListAsync("catalog", mode, 100, query, default, countOnly: true);
            Assert.Equal(ids, list.Items.Select(x => x.BggId));
            Assert.Equal(ids.Length, list.Total);
            Assert.Equal(list.Total, preview.Total);
            Assert.Empty(preview.Items);
        }
    }

    [Theory]
    [InlineData(2020, 2015)]
    [InlineData(0, null)]
    [InlineData(null, 10000)]
    public async Task InvalidYearRange_IsRejectedForListAndPreview(int? from, int? to)
    {
        await using var fixture = Fixture.Create();
        var query = new CatalogQuery(null, null, [], [], null, FromYear: from, ToYear: to);
        foreach (var countOnly in new[] { false, true })
            await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ListAsync("catalog", BotMode.Club, 100, query, default, countOnly));
    }

    private static ClubCollectionGame Game(long bggId, string name,
        IReadOnlyList<ClubCollectionExpansion>? expansions = null) =>
        new(bggId, name, null, null, 2, 4, "3", expansions ?? [], OriginalName: "Canonical English Name");

    private static CollectionItemSnapshot Snapshot(string name, IReadOnlyList<long>? parents = null) =>
        new(CollectionItemSnapshot.CurrentVersion, name, null, null, 2, 4, "3",
            ParentBggIds: parents, OriginalName: name);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db)
        {
            Db = db;
            var policy = new CampParticipationPolicy(db, TimeProvider.System);
            var contributions = new CampContributionSelectionService(db, policy, TimeProvider.System);
            Service = new GameCatalogService(db, new EffectiveCampCatalogService(db, contributions));
        }

        public AppDbContext Db { get; }
        public GameCatalogService Service { get; }

        public static Fixture Create() => new(new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));

        public Club AddClub(string key, params ClubCollectionGame[] games)
        {
            var club = new Club
            {
                BotChatKey = key, Name = key,
                CollectionJson = ClubCollectionSerializer.Serialize(
                    new ClubCollectionDocument(ClubCollectionDocument.CurrentVersion, games))
            };
            Db.Add(new OyinQCommunity
            {
                Key = key, Name = key, Mode = BotMode.Club, TimeZoneId = "UTC", IsActive = true,
                Club = club
            });
            return club;
        }

        public Camp AddCamp(string key, params ClubCollectionGame[] games)
        {
            var camp = new Camp
            {
                BotChatKey = key, Name = key, Status = CampStatus.Active,
                StartsAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), EndsAtUtc = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero),
                BaseCollectionJson = ClubCollectionSerializer.Serialize(
                    new ClubCollectionDocument(ClubCollectionDocument.CurrentVersion, games))
            };
            Db.Add(new OyinQCommunity
            {
                Key = key, Name = key, Mode = BotMode.Camp, TimeZoneId = "UTC", IsActive = true,
                Camp = camp
            });
            return camp;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
