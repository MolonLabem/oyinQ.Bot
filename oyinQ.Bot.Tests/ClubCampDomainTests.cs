using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class ClubCampDomainTests
{
    [Fact]
    public void Model_StructurallyPreventsOneChatFromBeingBothClubAndCamp()
    {
        using var dbContext = CreateDbContext();
        var designModel = dbContext.GetService<IDesignTimeModel>().Model;
        var club = designModel.FindEntityType(typeof(Club))!;
        var camp = designModel.FindEntityType(typeof(Camp))!;

        Assert.Contains(club.GetCheckConstraints(), value => value.Name == "CK_Clubs_BotChatMode" && value.Sql.Contains("= 0"));
        Assert.Contains(camp.GetCheckConstraints(), value => value.Name == "CK_Camps_BotChatMode" && value.Sql.Contains("= 1"));
        Assert.Contains(club.GetForeignKeys(), value =>
            value.Properties.Select(property => property.Name).SequenceEqual([nameof(Club.BotChatKey), nameof(Club.BotChatMode)])
            && value.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([nameof(OyinQCommunity.Key), nameof(OyinQCommunity.Mode)]));
        Assert.Contains(camp.GetForeignKeys(), value =>
            value.Properties.Select(property => property.Name).SequenceEqual([nameof(Camp.BotChatKey), nameof(Camp.BotChatMode)])
            && value.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([nameof(OyinQCommunity.Key), nameof(OyinQCommunity.Mode)]));
    }

    [Fact]
    public void Model_PreventsDuplicateContributionFromSameParticipant()
    {
        using var dbContext = CreateDbContext();
        var contribution = dbContext.Model.FindEntityType(typeof(CampGameContribution))!;

        Assert.Contains(contribution.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(value => value.Name).SequenceEqual([
                nameof(CampGameContribution.CampId),
                nameof(CampGameContribution.ParticipantId),
                nameof(CampGameContribution.BggId),
                nameof(CampGameContribution.ItemType)]));
    }

    [Fact]
    public void CampAndGatheringSnapshots_DoNotChangeWhenClubCollectionChanges()
    {
        var originalGame = Game(1, "Original");
        var club = new Club
        {
            CollectionJson = ClubCollectionSerializer.Serialize(new(1, [originalGame]))
        };
        var camp = new Camp { BaseCollectionJson = club.CollectionJson };
        var gatheringSnapshot = GatheringGameSnapshot.FromClubGame(originalGame, []);

        club.ReplaceCollection(new ClubCollectionDocument(1, [Game(2, "Replacement")]), DateTimeOffset.UtcNow);

        Assert.Equal("Original", camp.ReadBaseCollection().Games.Single().Name);
        Assert.Equal("Original", GatheringGameSnapshotSerializer.Deserialize(
            GatheringGameSnapshotSerializer.Serialize(gatheringSnapshot)).Name);
    }

    [Fact]
    public void ClubCollectionEdit_ChangesExactlyTheSelectedClubDocument()
    {
        var first = new ClubCollectionDocument(1, [Game(1, "First")]);
        var second = new ClubCollectionDocument(1, [Game(2, "Second")]);

        var editedFirst = ClubCollectionEditor.AddOrReplace(first, Game(3, "Third"));

        Assert.Equal([1L, 3L], editedFirst.Games.Select(value => value.BggId).Order());
        Assert.Equal([2L], second.Games.Select(value => value.BggId));
    }

    [Fact]
    public async Task ArbitraryBggGatheringSelection_DoesNotTrackPersistentCatalogEntities()
    {
        using var dbContext = CreateDbContext();
        var service = new GatheringGameSelectionService(dbContext, new FakeBggClient());

        var snapshot = await service.FromArbitraryBggAsync(42, [99], default);

        Assert.Equal(42, snapshot.BggId);
        Assert.Equal(99, Assert.Single(snapshot.SelectedExpansions).BggId);
        Assert.Equal(GameType.Thematic, snapshot.Type);
        Assert.Equal("Adventure", Assert.Single(snapshot.Categories!).Name);
        Assert.Equal("Cooperative Game", Assert.Single(snapshot.Mechanics!).Name);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ArbitraryBggGatheringSelection_NormalizesInvalidPlayerRange()
    {
        using var dbContext = CreateDbContext();
        var service = new GatheringGameSelectionService(dbContext, new FakeBggClient(0, 0));

        var snapshot = await service.FromArbitraryBggAsync(42, [], default);

        Assert.Equal(1, snapshot.MinPlayers);
        Assert.Equal(12, snapshot.MaxPlayers);
        Assert.True(snapshot.PlayerRangeDefaulted);
    }

    [Fact]
    public void AccessPolicy_RequiresRegistrationOnlyForCamp_AndScopesOrganizer()
    {
        var organizer = new Participant { TelegramUserId = 7 };
        var gathering = new GameGathering { CommunityKey = "club-a", OrganizerParticipant = organizer };

        Assert.False(GatheringAccessPolicy.RequiresRegistration(BotMode.Club));
        Assert.True(GatheringAccessPolicy.RequiresRegistration(BotMode.Camp));
    }

    [Fact]
    public void AccessPolicy_HidesContradictoryOrPastParticipationActions()
    {
        var now = DateTimeOffset.UtcNow;
        var gathering = new GameGathering
        {
            StartsAtUtc = now.AddHours(2),
            Status = GatheringStatus.Recruiting
        };

        Assert.True(GatheringAccessPolicy.CanJoin(gathering, false, false, now));
        Assert.False(GatheringAccessPolicy.CanJoin(gathering, false, true, now));
        Assert.True(GatheringAccessPolicy.CanLeave(gathering, false, true, now));
        Assert.False(GatheringAccessPolicy.CanLeave(gathering, true, true, now));

        gathering.StartsAtUtc = now.AddMinutes(-1);
        Assert.False(GatheringAccessPolicy.CanJoin(gathering, false, false, now));
        Assert.False(GatheringAccessPolicy.CanLeave(gathering, false, true, now));
    }

    private static ClubCollectionGame Game(long bggId, string name) =>
        new(bggId, name, null, null, 2, 4, "3-4", []);

    private static AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=oyinq_model_test;Username=test;Password=test")
            .Options);

    private sealed class FakeBggClient(int? minimumPlayers = 2, int? maximumPlayers = 4) : IBoardGameGeekClient
    {
        public Task<BggGameDetails?> GetGameDetailsAsync(long bggId, CancellationToken cancellationToken) =>
            Task.FromResult<BggGameDetails?>(new(
                new ExternalGame(bggId, "Transient", minimumPlayers, maximumPlayers, "3", null,
                    "https://example/thumb", "https://example/image",
                    Types: ["Thematic"], Categories: ["Adventure"], Description: "Description",
                    Subdomains: [new(5496, "Thematic Games")],
                    CategoryItems: [new(1022, "Adventure")],
                    Mechanics: [new(2023, "Cooperative Game")], Type: GameType.Thematic),
                [new BggExpansion(99, "Expansion")]));

        public Task<IReadOnlyList<ExternalGameSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(string username, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string username, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> bggIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
