using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class GatheringGameSelectionServiceTests
{
    [Theory]
    [InlineData(BotMode.Club)]
    [InlineData(BotMode.Camp)]
    public async Task SavedGame_UsesCanonicalDetailsAndAcceptsNonOwnedExpansion(BotMode mode)
    {
        await using var fixture = await Fixture.CreateAsync(mode, LocalGame(expansions: []),
            CanonicalDetails(new BggExpansion(99, "Каноническое дополнение")));

        var snapshot = await fixture.SelectAsync([99]);

        Assert.Equal("Каноническое название", snapshot.Name);
        Assert.Equal("Canonical Name", snapshot.OriginalName);
        Assert.Equal(99, Assert.Single(snapshot.SelectedExpansions).BggId);
        Assert.Equal(99, Assert.Single(snapshot.KnownExpansions!).BggId);
        Assert.Equal(1, fixture.Bgg.DetailRequests);
    }

    [Fact]
    public async Task SavedGame_RejectsExpansionLinkedToAnotherGame()
    {
        await using var fixture = await Fixture.CreateAsync(BotMode.Club, LocalGame(expansions: []),
            CanonicalDetails(new BggExpansion(99, "Правильное дополнение")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.SelectAsync([100]));

        Assert.Equal("Выбрано дополнение, которое не относится к этой игре.", error.Message);
    }

    [Fact]
    public async Task BggUnavailable_SavedGameAndKnownExpansionsRemainUsable()
    {
        var localExpansion = new ClubCollectionExpansion(77, "Локальное дополнение");
        await using var fixture = await Fixture.CreateAsync(BotMode.Club, LocalGame([localExpansion]),
            details: null, unavailable: true);

        var snapshot = await fixture.SelectAsync([77]);

        Assert.Equal("Локальная игра", snapshot.Name);
        Assert.Equal(77, Assert.Single(snapshot.SelectedExpansions).BggId);
        Assert.Equal(1, fixture.Bgg.DetailRequests);
    }

    [Fact]
    public async Task BggHasNoCurrentDetails_SavedGameAndKnownExpansionsRemainUsable()
    {
        var localExpansion = new ClubCollectionExpansion(78, "Сохранённое дополнение");
        await using var fixture = await Fixture.CreateAsync(BotMode.Club, LocalGame([localExpansion]),
            details: null);

        var snapshot = await fixture.SelectAsync([78]);

        Assert.Equal("Локальная игра", snapshot.Name);
        Assert.Equal(78, Assert.Single(snapshot.SelectedExpansions).BggId);
        Assert.Equal(1, fixture.Bgg.DetailRequests);
    }

    [Fact]
    public async Task ExternalBggGame_ExposesExpansionsAndOwnershipFromOneFetch()
    {
        await using var fixture = await Fixture.CreateAsync(BotMode.Club, LocalGame([]), CanonicalDetails(new BggExpansion(99, "Дополнение")));
        var selection = await fixture.Selection.ExternalSelectionAsync(42, [99], default);
        Assert.Equal(99, Assert.Single(selection.Snapshot.KnownExpansions!).BggId);
        Assert.Equal(99, Assert.Single(selection.Snapshot.SelectedExpansions).BggId);
        Assert.Equal(2, selection.Ownership.Count);
        Assert.Equal(42, selection.Ownership.Single(x => x.ItemType == CollectionItemType.Expansion).ParentBggId);
        Assert.Equal(1, fixture.Bgg.DetailRequests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Selection.FromArbitraryBggAsync(42, [100], default));
    }
    [Fact]
    public async Task ExternalBggUnavailable_DoesNotInventExpansionMetadata()
    {
        await using var fixture = await Fixture.CreateAsync(BotMode.Club, LocalGame([]), null, unavailable: true);
        await Assert.ThrowsAsync<BggUnavailableException>(() => fixture.Selection.ExternalSelectionAsync(42, [99], default));
        Assert.Empty(fixture.Db.ParticipantCollectionItems);
    }

    private static ClubCollectionGame LocalGame(
        IReadOnlyList<ClubCollectionExpansion>? expansions = null) => new(
        42, "Локальная игра", null, null, 2, 4, "3", expansions ?? [],
        OriginalName: "Local Game");

    private static BggGameDetails CanonicalDetails(params BggExpansion[] expansions) => new(
        new ExternalGame(42, "Каноническое название", 1, 5, "4", BggGameUrl.FromId(42),
            OriginalName: "Canonical Name"), expansions);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db, BotMode mode, FakeBggClient bgg,
            GatheringGameSelectionService selection)
        {
            Db = db;
            Mode = mode;
            Bgg = bgg;
            Selection = selection;
        }

        public AppDbContext Db { get; }
        public BotMode Mode { get; }
        public FakeBggClient Bgg { get; }
        public GatheringGameSelectionService Selection { get; }

        public Task<GatheringGameSnapshot> SelectAsync(IReadOnlyCollection<long> expansionIds) =>
            Mode == BotMode.Club
                ? Selection.FromClubCollectionAsync("community", 42, expansionIds, default)
                : Selection.FromCampCatalogAsync("community", 42, expansionIds, default);

        public static async Task<Fixture> CreateAsync(BotMode mode, ClubCollectionGame localGame,
            BggGameDetails? details, bool unavailable = false)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var document = ClubCollectionSerializer.Serialize(
                new ClubCollectionDocument(ClubCollectionDocument.CurrentVersion, [localGame]));
            var community = new OyinQCommunity
            {
                Key = "community", Name = "Сообщество", Mode = mode, TimeZoneId = "UTC", IsActive = true
            };
            if (mode == BotMode.Club)
                community.Club = new Club
                {
                    BotChatKey = community.Key, Name = community.Name, CollectionJson = document
                };
            else
                community.Camp = new Camp
                {
                    BotChatKey = community.Key, Name = community.Name, Status = CampStatus.Active,
                    BaseCollectionJson = document
                };
            db.OyinQCommunities.Add(community);
            await db.SaveChangesAsync();
            var bgg = new FakeBggClient(details, unavailable);
            var policy = new CampParticipationPolicy(db, TimeProvider.System);
            var contributions = new CampContributionSelectionService(db, policy, TimeProvider.System);
            var catalog = new EffectiveCampCatalogService(db, contributions);
            var selection = new GatheringGameSelectionService(db, bgg, catalog,
                NullLogger<GatheringGameSelectionService>.Instance);
            return new(db, mode, bgg, selection);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeBggClient(BggGameDetails? details, bool unavailable) : IBoardGameGeekClient
    {
        public int DetailRequests { get; private set; }

        public Task<BggGameDetails?> GetGameDetailsAsync(long bggId, CancellationToken cancellationToken)
        {
            DetailRequests++;
            return unavailable
                ? Task.FromException<BggGameDetails?>(new BggUnavailableException("BGG недоступен"))
                : Task.FromResult(details);
        }

        public Task<IReadOnlyList<BggBaseGameSearchResult>> SearchAsync(string query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(string username,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string username,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> bggIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
