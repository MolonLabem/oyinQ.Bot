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
        camp.Contributions.Add(new CampGameContribution
        {
            Participant = participant, BggId = 10, ItemType = CampContributionItemType.BaseGame,
            Source = CampContributionSource.Manual, Commitment = CampBringCommitment.Bringing,
            SnapshotJson = CampContributionSnapshotSerializer.Serialize(Snapshot("Brass"))
        });
        camp.Contributions.Add(new CampGameContribution
        {
            Participant = participant, BggId = 30, ItemType = CampContributionItemType.Expansion,
            Source = CampContributionSource.Manual, Commitment = CampBringCommitment.Available,
            ParentBggId = 10,
            SnapshotJson = CampContributionSnapshotSerializer.Serialize(Snapshot("Brass: Birmingham", [10]))
        });
        await fixture.Db.SaveChangesAsync();

        var details = await fixture.Service.DetailsAsync("camp", BotMode.Camp, 100, 10, default);

        Assert.True(details.Availability.IsInBaseCollection);
        Assert.True(details.Availability.HasCommittedProvider);
        Assert.Equal("Игрок", Assert.Single(details.Availability.Providers).DisplayName);
        Assert.Equal(30, Assert.Single(details.Expansions).BggId);
    }

    private static ClubCollectionGame Game(long bggId, string name,
        IReadOnlyList<ClubCollectionExpansion>? expansions = null) =>
        new(bggId, name, null, null, 2, 4, "3", expansions ?? [], OriginalName: "Canonical English Name");

    private static CampContributionSnapshot Snapshot(string name, IReadOnlyList<long>? parents = null) =>
        new(CampContributionSnapshot.CurrentVersion, name, null, null, 2, 4, "3",
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
