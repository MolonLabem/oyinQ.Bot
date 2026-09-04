using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Tests;

public sealed class AppDbContextMigrationTests
{
    private const string CleanBaselineMigration = "20260901073247_CleanBaseline";
    private const string ForumPostingTopicsMigration = "20260901125924_ForumPostingTopics";
    private const string CommunityDeletionMigration = "20260901133621_CommunityDeletionAndTelegramPhotos";
    private const string GatheringGuestsMigration = "20260903073138_AddGatheringGuests";

    [Fact]
    public void Migrations_ContainCleanBaselineAndAdditiveForumTopicConfiguration()
    {
        using var dbContext = CreateDbContext();

        Assert.Equal([CleanBaselineMigration, ForumPostingTopicsMigration, CommunityDeletionMigration,
            GatheringGuestsMigration, "20260904072034_PersistentParticipantCollection", "20260904080727_CampOperatingInstants", "20260904081750_NotificationDelivery", "20260904084352_GatheringPlayRecords", "20260904092743_PlayOutcomesReferencesAndReleases"], dbContext.Database.GetMigrations());
    }

    [Fact]
    public void Model_ExcludesRetiredLegacyStructures()
    {
        using var dbContext = CreateDbContext();
        var entityNames = dbContext.Model.GetEntityTypes().Select(x => x.ClrType.Name).ToHashSet();

        Assert.DoesNotContain("Game", entityNames);
        Assert.DoesNotContain("GameCopy", entityNames);
        Assert.DoesNotContain("GameInterest", entityNames);
        Assert.DoesNotContain("GameSession", entityNames);
        Assert.DoesNotContain("GameSessionParticipant", entityNames);
        Assert.DoesNotContain("CollectionImport", entityNames);
        Assert.DoesNotContain("ParticipantConversationState", entityNames);
        Assert.DoesNotContain("OyinQAdministrator", entityNames);

        Assert.Null(dbContext.Model.FindEntityType(typeof(Participant))?.FindProperty("DaysStaying"));
        Assert.Null(dbContext.Model.FindEntityType(typeof(Participant))?.FindProperty("NeedsAccommodation"));
        Assert.Null(dbContext.Model.FindEntityType(typeof(Club))?.FindProperty("BggUsername"));
        Assert.Null(dbContext.Model.FindEntityType(typeof(GameGathering))?.FindProperty("GameId"));
    }

    [Fact]
    public void Model_ConfiguresCurrentCollectionAndGatheringSchema()
    {
        using var dbContext = CreateDbContext();
        var participant = dbContext.Model.FindEntityType(typeof(Participant));
        var gathering = dbContext.Model.FindEntityType(typeof(GameGathering));

        Assert.True(participant?.FindProperty(nameof(Participant.PreferredDisplayName))?.IsNullable);
        Assert.Equal(128, participant?.FindProperty(nameof(Participant.PreferredDisplayName))?.GetMaxLength());
        Assert.Equal("jsonb", dbContext.Model.FindEntityType(typeof(Club))?
            .FindProperty(nameof(Club.CollectionJson))?.GetColumnType());
        Assert.Equal("jsonb", gathering?.FindProperty(nameof(GameGathering.GameSnapshotJson))?.GetColumnType());
        Assert.Equal(300, gathering?.FindProperty(nameof(GameGathering.Description))?.GetMaxLength());
        Assert.NotNull(gathering?.FindProperty(nameof(GameGathering.CanTeachRules)));
        var guest = dbContext.Model.FindEntityType(typeof(GameGatheringGuest));
        Assert.Equal(80, guest?.FindProperty(nameof(GameGatheringGuest.DisplayName))?.GetMaxLength());
        Assert.Equal(DeleteBehavior.Cascade, guest!.GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == typeof(GameGathering)).DeleteBehavior);
    }

    [Fact]
    public void Model_ConfiguresDurableBackgroundWork()
    {
        using var dbContext = CreateDbContext();

        Assert.Equal("jsonb", dbContext.Model.FindEntityType(typeof(CampBggImport))?
            .FindProperty(nameof(CampBggImport.DraftJson))?.GetColumnType());
        Assert.Equal(2000, dbContext.Model.FindEntityType(typeof(TelegramMessageCleanup))?
            .FindProperty(nameof(TelegramMessageCleanup.LastError))?.GetMaxLength());
        Assert.NotNull(dbContext.Model.FindEntityType(typeof(PendingTelegramPeerSelection)));
        Assert.NotNull(dbContext.Model.FindEntityType(typeof(ClubMetadataRefresh)));
    }

    [Fact]
    public void Model_ConfiguresExactCampAttendanceAndScopedAdministration()
    {
        using var dbContext = CreateDbContext();
        var day = dbContext.Model.FindEntityType(typeof(CampRegistrationDay));
        var permission = dbContext.Model.FindEntityType(typeof(ChatAdminPermission));
        var registrationDisplayName = dbContext.Model.FindEntityType(typeof(CampRegistration))?
            .FindProperty(nameof(CampRegistration.DisplayName));

        Assert.Equal([nameof(CampRegistrationDay.CampRegistrationId), nameof(CampRegistrationDay.Date)],
            day!.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal(DeleteBehavior.Cascade, day.GetForeignKeys().Single().DeleteBehavior);
        Assert.True(registrationDisplayName?.IsNullable);
        Assert.Equal(128, registrationDisplayName?.GetMaxLength());
        Assert.NotNull(dbContext.Model.FindEntityType(typeof(KnownTelegramChat)));
        var topics = dbContext.Model.FindEntityType(typeof(TelegramForumTopic));
        Assert.Equal([nameof(TelegramForumTopic.TelegramChatId), nameof(TelegramForumTopic.MessageThreadId)],
            topics!.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.True(dbContext.Model.FindEntityType(typeof(OyinQCommunity))?
            .FindProperty(nameof(OyinQCommunity.PostingMessageThreadId))?.IsNullable);
        Assert.True(dbContext.Model.FindEntityType(typeof(OyinQCommunity))?
            .FindProperty(nameof(OyinQCommunity.DeletedAt))?.IsNullable);
        Assert.Equal(256, dbContext.Model.FindEntityType(typeof(KnownTelegramChat))?
            .FindProperty(nameof(KnownTelegramChat.TelegramPhotoFileId))?.GetMaxLength());
        Assert.Contains(dbContext.Model.FindEntityType(typeof(OyinQCommunity))!.GetIndexes(), index =>
            index.IsUnique && index.GetFilter() == "\"DeletedAt\" IS NULL"
            && index.Properties.Select(x => x.Name).SequenceEqual([nameof(OyinQCommunity.TelegramChatId)]));
        Assert.Contains(permission!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(
                [nameof(ChatAdminPermission.TelegramUserId), nameof(ChatAdminPermission.CommunityKey)]));
    }

    [Fact]
    public void Model_ConfiguresOneActiveImportJobPerOwner()
    {
        using var dbContext = CreateDbContext();
        var clubImport = dbContext.Model.FindEntityType(typeof(ClubBggImport));
        var campImport = dbContext.Model.FindEntityType(typeof(CampBggImport));

        Assert.Equal(100, clubImport?.FindProperty(nameof(ClubBggImport.BggUsername))?.GetMaxLength());
        Assert.Contains(clubImport!.GetIndexes(), index => index.IsUnique
            && index.GetFilter() == "\"Status\" IN (0, 1)"
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(ClubBggImport.ClubId)]));
        Assert.Contains(campImport!.GetIndexes(), index => index.IsUnique
            && index.GetFilter() == "\"Status\" IN (0, 1)"
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(CampBggImport.CampId), nameof(CampBggImport.ParticipantId)]));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=oyinq_model_test;Username=test;Password=test")
            .Options;

        return new AppDbContext(options);
    }
}
