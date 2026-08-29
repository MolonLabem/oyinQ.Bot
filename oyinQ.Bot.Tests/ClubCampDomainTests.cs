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
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public void ImportSelection_DefaultsToAll_AndExpansionSelectionIsIndependent()
    {
        CampImportSelectionItem[] imported =
        [
            new(10, CampContributionItemType.BaseGame, null, "Base", false),
            new(20, CampContributionItemType.Expansion, 10, "Expansion", false)
        ];

        var selected = CampContributionSelectionService.SelectAll(imported);
        var baseOff = selected.Select(value => value.BggId == 10 ? value with { Selected = false } : value).ToArray();

        Assert.All(selected, value => Assert.True(value.Selected));
        Assert.True(baseOff.Single(value => value.BggId == 20).Selected);
        Assert.True(CampContributionSelectionService.NeedsMissingBaseWarning(baseOff[1], baseOff));
        Assert.True(CampBggImportService.BuildGroups(baseOff).Single().ShowMissingBaseWarning);
    }

    [Fact]
    public void MultipleContributorsBecomeOneLogicalCatalogItem_AndKeepContributors()
    {
        CampGameContribution[] values =
        [
            new() { BggId = 10, ItemType = CampContributionItemType.BaseGame, ParticipantId = 1,
                SnapshotJson = CampContributionSnapshotSerializer.Serialize(new(1, "Brass", null, null, 2, 4, null)) },
            new() { BggId = 10, ItemType = CampContributionItemType.BaseGame, ParticipantId = 2,
                SnapshotJson = CampContributionSnapshotSerializer.Serialize(new(1, "Brass", null, null, 2, 4, null)) }
        ];

        var item = Assert.Single(CampContributionSelectionService.MergeContributions(values));

        Assert.Equal("Brass", item.Name);
        Assert.Equal([1L, 2L], item.ContributorParticipantIds);
    }

    [Fact]
    public void AccessPolicy_RequiresRegistrationOnlyForCamp_AndScopesOrganizer()
    {
        var organizer = new Participant { TelegramUserId = 7 };
        var gathering = new GameGathering { CommunityKey = "club-a", OrganizerParticipant = organizer };

        Assert.False(GatheringAccessPolicy.RequiresRegistration(BotMode.Club));
        Assert.True(GatheringAccessPolicy.RequiresRegistration(BotMode.Camp));
        Assert.True(GatheringAccessPolicy.CanManage(gathering, "club-a", 7));
        Assert.False(GatheringAccessPolicy.CanManage(gathering, "club-b", 7));
        Assert.False(GatheringAccessPolicy.CanManage(gathering, "club-a", 8));
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

    private sealed class FakeBggClient : IBoardGameGeekClient
    {
        public Task<BggGameDetails?> GetGameDetailsAsync(long bggId, CancellationToken cancellationToken) =>
            Task.FromResult<BggGameDetails?>(new(
                new ExternalGame(bggId, "Transient", 2, 4, "3", null, "https://example/thumb", "https://example/image"),
                [new BggExpansion(99, "Expansion")]));

        public Task<IReadOnlyList<ExternalGameSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExternalGame?> GetGameAsync(long bggId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedCollectionAsync(string username, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExternalCollectionStep> GetOwnedCollectionStepAsync(string username, int offset, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
