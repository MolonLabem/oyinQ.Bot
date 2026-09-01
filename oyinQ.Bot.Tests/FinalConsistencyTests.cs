using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class FinalConsistencyTests
{
    [Fact]
    public void RegistrationCompleteness_RejectsMigratedMissingFields()
    {
        var camp = Camp();
        Assert.False(CampParticipationPolicy.IsRegistrationComplete(
            new CampRegistration { DaysStaying = 2, NeedsAccommodation = false, City = null }, camp));
        Assert.False(CampParticipationPolicy.IsRegistrationComplete(
            new CampRegistration { DaysStaying = null, NeedsAccommodation = false, City = "Астана" }, camp));
        Assert.False(CampParticipationPolicy.IsRegistrationComplete(
            new CampRegistration { DaysStaying = 2, NeedsAccommodation = null, City = "Астана" }, camp));
    }

    [Fact]
    public void RegistrationCompleteness_AcceptsOnlyValidCurrentRegistration()
    {
        var camp = Camp();
        Assert.True(CampParticipationPolicy.IsRegistrationComplete(
            Registration([new(2026, 8, 29), new(2026, 8, 30)]), camp));
        Assert.False(CampParticipationPolicy.IsRegistrationComplete(
            new CampRegistration { DaysStaying = 4, NeedsAccommodation = false, City = "Астана" }, camp));
    }

    private static CampRegistration Registration(IReadOnlyCollection<DateOnly> days)
    {
        var registration = new CampRegistration { DaysStaying = days.Count, NeedsAccommodation = false, City = " Астана " };
        foreach (var date in days) registration.SelectedDays.Add(new CampRegistrationDay { Date = date });
        return registration;
    }

    [Fact]
    public void CampDateBoundary_UsesLocalInclusiveEndDate()
    {
        var camp = Camp();
        Assert.False(CampParticipationPolicy.HasEnded(camp, "Asia/Qyzylorda",
            new DateTimeOffset(2026, 8, 31, 18, 59, 59, TimeSpan.Zero)));
        Assert.True(CampParticipationPolicy.HasEnded(camp, "Asia/Qyzylorda",
            new DateTimeOffset(2026, 8, 31, 19, 0, 0, TimeSpan.Zero)));
        Assert.Throws<InvalidOperationException>(() => CampParticipationPolicy.EnsureAcceptsMutations(
            camp, "Asia/Qyzylorda", new DateTimeOffset(2026, 8, 31, 19, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void TimeZoneChange_IsAllowedBeforeAndRejectedAfterFirstGathering()
    {
        CommunityTimeZonePolicy.EnsureChangeAllowed("Asia/Qyzylorda", "Asia/Almaty", false);
        CommunityTimeZonePolicy.EnsureChangeAllowed("Asia/Qyzylorda", "Asia/Qyzylorda", true);
        Assert.Throws<InvalidOperationException>(() => CommunityTimeZonePolicy.EnsureChangeAllowed(
            "Asia/Qyzylorda", "Asia/Almaty", true));
    }

    [Fact]
    public void ImportConfirmation_PersistsOnlySelectedOverridesAndRoundTrips()
    {
        var draft = new CampBggImportDraft(3, "owner", [
            Item(1), Item(2, CampImportSkipReason.AlreadyInBaseCollection, true),
            Item(3, CampImportSkipReason.AlreadyInBaseCollection, true)]);

        var first = CampBggImportCoordinator.BuildConfirmation(draft, [1, 2], []);
        var restored = CampBggImportConfirmationSerializer.Deserialize(
            CampBggImportConfirmationSerializer.Serialize(first));

        Assert.Equal([1L, 2L], restored.SelectedBaseGameIds);
        Assert.Equal(1, restored.Added);
        Assert.Equal([new CampImportItemKey(2, CampContributionItemType.BaseGame)],
            restored.SelectedOverridableItems);
        Assert.DoesNotContain(restored.SelectedOverridableItems, x => x.BggId == 3);
    }

    [Fact]
    public void ImportConfirmation_IsIndependentOfLaterRetrySelection()
    {
        var draft = new CampBggImportDraft(3, "owner", [Item(1), Item(2)]);
        var persisted = CampBggImportCoordinator.BuildConfirmation(draft, [1], []);
        var retryWouldHaveProduced = CampBggImportCoordinator.BuildConfirmation(draft, [2], []);

        Assert.Equal([1L], CampBggImportConfirmationSerializer.Deserialize(
            CampBggImportConfirmationSerializer.Serialize(persisted)).SelectedBaseGameIds);
        Assert.Equal([2L], retryWouldHaveProduced.SelectedBaseGameIds);
    }

    [Fact]
    public void ImportOverride_IsScopedToOwnerAndCamp()
    {
        var import = new CampBggImport { CampId = 4, ParticipantId = 7 };
        CampBggImportCoordinator.EnsureOwner(import, 4, 7);
        Assert.Throws<UnauthorizedAccessException>(() =>
            CampBggImportCoordinator.EnsureOwner(import, 4, 8));
        Assert.Throws<UnauthorizedAccessException>(() =>
            CampBggImportCoordinator.EnsureOwner(import, 5, 7));
    }

    [Fact]
    public void EffectiveCampCatalog_ProducesOneBaseAndAttachesMultiParentExpansionToBothGames()
    {
        var baseCollection = new ClubCollectionDocument(ClubCollectionDocument.CurrentVersion,
        [
            Game(10, "Base A"), Game(20, "Base B")
        ]);
        var provider = new CampCatalogProvider(7, "Игрок", "Алматы", CampContributionSource.Manual,
            CampBringCommitment.Bringing);
        var contributions = new[]
        {
            new EffectiveCampCatalogItem(10, CampContributionItemType.BaseGame, [], Snapshot("Base A"), 1, [provider]),
            new EffectiveCampCatalogItem(30, CampContributionItemType.Expansion, [10, 20], Snapshot("Expansion X"), 1, [provider])
        };

        var result = EffectiveCampCatalogService.Build(baseCollection, contributions);

        Assert.Equal(2, result.Count);
        Assert.Single(result, x => x.Game.BggId == 10);
        Assert.Contains(result.Single(x => x.Game.BggId == 10).Expansions, x => x.BggId == 30);
        Assert.Contains(result.Single(x => x.Game.BggId == 20).Expansions, x => x.BggId == 30);
        Assert.Single(result.Single(x => x.Game.BggId == 10).Providers);
    }

    [Fact]
    public void UnderfilledNotification_CapturesOrganizerAndConfirmedParticipantsOnly()
    {
        var organizer = new Participant { TelegramUserId = 1, DisplayName = "Организатор" };
        var confirmed = new Participant { TelegramUserId = 2, DisplayName = "Участник" };
        var waitlisted = new Participant { TelegramUserId = 3, DisplayName = "Ожидающий" };
        var gathering = new GameGathering
        {
            OrganizerParticipant = organizer, MinimumPlayers = 4,
            GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new GatheringGameSnapshot(
                GatheringGameSnapshot.CurrentVersion, 10, "Brass", null, null, 2, 4, null, [],
                KnownExpansions: [])),
            Participants =
            [
                new GameGatheringParticipant { Status = GatheringParticipationStatus.Confirmed, Participant = confirmed },
                new GameGatheringParticipant { Status = GatheringParticipationStatus.Waitlisted, Participant = waitlisted }
            ]
        };

        var notification = GatheringNotificationService.CaptureUnderfilled(gathering);

        Assert.Equal([1L, 2L], notification.TelegramUserIds);
        Assert.Equal(2, notification.ConfirmedPlayers);
    }

    [Fact]
    public void TrustedIdentityRefresh_PreservesPreferredNameAndMissingTelegramDisplayName()
    {
        var participant = new Participant { TelegramUserId = 42, TelegramUsername = "old",
            DisplayName = "Old name", PreferredDisplayName = "Игровое имя" };
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");

        Assert.True(ParticipantIdentityPolicy.RefreshTrustedPresentation(participant, "new", "New name", now));
        Assert.Equal("new", participant.TelegramUsername);
        Assert.Equal("New name", participant.DisplayName);
        Assert.Equal("Игровое имя", participant.PreferredDisplayName);
        Assert.False(ParticipantIdentityPolicy.RefreshTrustedPresentation(participant, "new", null, now.AddMinutes(1)));
        Assert.Equal("New name", participant.DisplayName);
    }

    [Theory]
    [InlineData(null, null, ManagedChatMigrationAction.Ignore)]
    [InlineData(null, "camp", ManagedChatMigrationAction.Replay)]
    [InlineData("camp", null, ManagedChatMigrationAction.Update)]
    [InlineData("camp", "camp", ManagedChatMigrationAction.Replay)]
    [InlineData("camp", "club", ManagedChatMigrationAction.Collision)]
    public void TelegramChatMigration_ClassifiesUnmanagedReplayUpdateAndCollision(
        string? oldKey, string? newKey, ManagedChatMigrationAction expected) =>
        Assert.Equal(expected, ManagedCommunityService.ClassifyChatMigration(oldKey, newKey));

    [Fact]
    public void Model_EnforcesActiveJobInvariantsAndPersistsLeaseAndConfirmation()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model;Username=test;Password=test").Options);
        var import = db.Model.FindEntityType(typeof(CampBggImport))!;
        var refresh = db.Model.FindEntityType(typeof(ClubMetadataRefresh))!;

        Assert.Contains(import.GetIndexes(), x => x.IsUnique
            && x.GetDatabaseName() == "IX_CampBggImports_ActiveCampParticipant"
            && x.GetFilter() == "\"Status\" IN (0, 1)");
        Assert.Equal("jsonb", import.FindProperty(nameof(CampBggImport.ConfirmationJson))!.GetColumnType());
        Assert.Contains(refresh.GetIndexes(), x => x.IsUnique
            && x.GetDatabaseName() == "IX_ClubMetadataRefreshes_ActiveClub"
            && x.GetFilter() == "\"Status\" IN (0, 1)");
        Assert.NotNull(refresh.FindProperty(nameof(ClubMetadataRefresh.LeaseId)));
        Assert.NotNull(refresh.FindProperty(nameof(ClubMetadataRefresh.LeaseExpiresAt)));
        Assert.Contains("20260830210951_FinalConsistencyAndReliability", db.Database.GetMigrations());
    }

    [Fact]
    public void CurrentCampExports_IncludeRegistrationAndContributionFields()
    {
        Assert.Contains("city", CsvExportService.CampRegistrationHeaders);
        Assert.Contains("selected_dates", CsvExportService.CampRegistrationHeaders);
        Assert.Contains("source", CsvExportService.CampContributionHeaders);
        Assert.Contains("commitment", CsvExportService.CampContributionHeaders);
        Assert.Contains("parent_bgg_ids", CsvExportService.CampContributionHeaders);
    }

    private static Camp Camp() => new()
    {
        Status = CampStatus.Active, StartDate = new DateOnly(2026, 8, 29), EndDate = new DateOnly(2026, 8, 31)
    };

    private static CampBggImportDraftItem Item(long id, CampImportSkipReason? skip = null,
        bool overridable = false) => new(id, CampContributionItemType.BaseGame, null, Snapshot($"Game {id}"),
            SkipReason: skip, IsOverridable: overridable);

    private static CampContributionSnapshot Snapshot(string name) => new(
        CampContributionSnapshot.CurrentVersion, name, null, null, 2, 4, null);

    private static ClubCollectionGame Game(long id, string name) => new(id, name, null, null, 2, 4, null, []);
}
