using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class StabilizationDomainTests
{
    [Fact]
    public void ClubMutation_IncrementsRevisionAndDoesNotTouchAnotherClub()
    {
        var first = new Club { CollectionRevision = 3 };
        var second = new Club { CollectionRevision = 8 };
        first.ReplaceCollection(new(1, [Game(10, "Brass")]), DateTimeOffset.UtcNow);
        Assert.Equal(4, first.CollectionRevision);
        Assert.Equal(8, second.CollectionRevision);
    }

    [Fact]
    public void ClubCollection_ExportImportRoundTripsExpansions()
    {
        var original = new ClubCollectionDocument(1,
            [Game(10, "Base") with { Expansions = [new(20, "Expansion")] }]);
        var restored = ClubCollectionSerializer.Deserialize(ClubCollectionSerializer.Serialize(original));
        Assert.Equal(20, Assert.Single(Assert.Single(restored.Games).Expansions).BggId);
    }

    [Fact]
    public void ClubCollection_BaseGameWithoutExpansions_DoesNotReportAvailability()
    {
        var game = Game(10, "Base");

        Assert.False(game.HasExpansions);
    }

    [Fact]
    public void ClubCollection_GameWithExpansions_ReportsAvailability()
    {
        var game = Game(10, "Base") with
        {
            Expansions = [new ClubCollectionExpansion(20, "Expansion")]
        };

        Assert.True(game.HasExpansions);
    }

    [Theory]
    [InlineData("{\"version\":2,\"games\":[{\"bggId\":10,\"name\":\"Base\"}]}")]
    [InlineData("{\"version\":2,\"games\":[{\"bggId\":10,\"name\":\"Base\",\"expansions\":null}]}")]
    public void ClubCollection_MissingOrNullExpansions_NormalizesToEmpty(string json)
    {
        var game = Assert.Single(ClubCollectionSerializer.Deserialize(json).Games);

        Assert.Empty(game.Expansions);
        Assert.False(game.HasExpansions);
    }

    [Fact]
    public void ContributionSnapshot_RejectsUnsupportedVersionAndMalformedMetadata()
    {
        Assert.Throws<InvalidOperationException>(() => CampContributionSnapshotSerializer.Serialize(
            new(9, "Game", null, null, 2, 4, null)));
        Assert.Throws<InvalidOperationException>(() => CampContributionSnapshotSerializer.Serialize(
            new(1, "Game", null, null, 5, 2, null)));
    }

    [Fact]
    public void CollectionAndContributionSnapshots_AcceptLegacyJsonWithoutTags()
    {
        var collection = ClubCollectionSerializer.Deserialize("""
            {"version":1,"games":[{"bggId":10,"name":"Base","minPlayers":2,"maxPlayers":4,"expansions":[]}]}
            """);
        var contribution = CampContributionSnapshotSerializer.Deserialize("""
            {"version":1,"name":"Base","minPlayers":2,"maxPlayers":4}
            """);

        Assert.Null(Assert.Single(collection.Games).Types);
        Assert.Null(contribution.Categories);
    }

    [Fact]
    public void ImportDraft_DefaultsAllItemsAndRoundTripsAuthoritatively()
    {
        var draft = new CampBggImportDraft(1, "owner", [
            new(10, CampContributionItemType.BaseGame, null, Snapshot("Base")),
            new(20, CampContributionItemType.Expansion, 10, Snapshot("Expansion"))]);
        var restored = CampBggImportDraftSerializer.Deserialize(CampBggImportDraftSerializer.Serialize(draft));
        Assert.All(restored.Items, item => Assert.True(item.SelectedByDefault));
        Assert.Equal([10L, 20L], restored.Items.Select(x => x.BggId));
    }

    [Fact]
    public void GatheringCapacityEdit_RejectsMaximumBelowConfirmedCount()
    {
        var gathering = FutureGathering();
        gathering.Participants.Add(new() { Status = GatheringParticipationStatus.Confirmed });
        gathering.Participants.Add(new() { Status = GatheringParticipationStatus.Confirmed });
        Assert.Throws<InvalidOperationException>(() => GatheringRules.Update(gathering,
            gathering.StartsAtUtc.AddHours(1), 1, 2, 2, null, true, [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void GatheringCloseReopenAndCancel_UseExplicitLifecycle()
    {
        var gathering = FutureGathering();
        GatheringRules.Close(gathering, DateTimeOffset.UtcNow);
        Assert.Equal(GatheringStatus.Closed, gathering.Status);
        GatheringRules.Reopen(gathering, DateTimeOffset.UtcNow);
        Assert.Equal(GatheringStatus.Ready, gathering.Status);
        GatheringRules.Cancel(gathering, "Причина", DateTimeOffset.UtcNow);
        Assert.Equal(GatheringStatus.Cancelled, gathering.Status);
        Assert.Equal("Причина", gathering.CancellationReason);
    }

    [Fact]
    public void GatheringExpansionEdit_RejectsUnknownExpansion()
    {
        var gathering = FutureGathering();
        Assert.Throws<InvalidOperationException>(() => GatheringRules.Update(gathering,
            gathering.StartsAtUtc, 1, 2, 4, null, true, [999], DateTimeOffset.UtcNow));
    }

    private static ClubCollectionGame Game(long id, string name) => new(id, name, null, null, 2, 4, null, []);
    private static CampContributionSnapshot Snapshot(string name) => new(1, name, null, null, 2, 4, null);
    private static GameGathering FutureGathering()
    {
        var snapshot = new GatheringGameSnapshot(2, 10, "Game", null, null, 1, 4, null, [], "catalog", []);
        return GatheringRules.Create("club", snapshot, 1, DateTimeOffset.UtcNow.AddDays(2),
            1, 2, 4, null, true, DateTimeOffset.UtcNow);
    }
}
