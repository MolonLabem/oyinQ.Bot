using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Tests;

public sealed class AppDbContextMigrationTests
{
    [Fact]
    public void Model_ConfiguresPreferredDisplayName_AsNullableMax128()
    {
        using var dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(typeof(Participant));
        var property = entityType?.FindProperty(nameof(Participant.PreferredDisplayName));

        Assert.NotNull(property);
        Assert.True(property.IsNullable);
        Assert.Equal(128, property.GetMaxLength());
    }

    [Fact]
    public void Migrations_IncludePreferredDisplayNameMigration()
    {
        using var dbContext = CreateDbContext();

        Assert.Contains(
            "20260819090000_PreferredDisplayName",
            dbContext.Database.GetMigrations());
    }

    [Fact]
    public void Model_ConfiguresGatheringPresentationLimitsAndGameImages()
    {
        using var dbContext = CreateDbContext();

        var gathering = dbContext.Model.FindEntityType(typeof(GameGathering));
        var game = dbContext.Model.FindEntityType(typeof(Game));

        Assert.Equal(300, gathering?.FindProperty(nameof(GameGathering.Description))?.GetMaxLength());
        Assert.NotNull(gathering?.FindProperty(nameof(GameGathering.CanTeachRules)));
        Assert.Equal(1000, game?.FindProperty(nameof(Game.ImageUrl))?.GetMaxLength());
        Assert.Equal(1000, game?.FindProperty(nameof(Game.ThumbnailImageUrl))?.GetMaxLength());
    }

    [Fact]
    public void Migrations_IncludeGatheringPresentationMigration()
    {
        using var dbContext = CreateDbContext();

        Assert.Contains(
            "20260828165036_AddGatheringPresentation",
            dbContext.Database.GetMigrations());
    }

    [Fact]
    public void Migrations_IncludeClubCampContextMigration()
    {
        using var dbContext = CreateDbContext();

        Assert.Contains(
            "20260828183821_ClubCampContextsAndGatheringSnapshots",
            dbContext.Database.GetMigrations());
        Assert.Equal("jsonb", dbContext.Model.FindEntityType(typeof(Club))?
            .FindProperty(nameof(Club.CollectionJson))?.GetColumnType());
        Assert.Equal("jsonb", dbContext.Model.FindEntityType(typeof(GameGathering))?
            .FindProperty(nameof(GameGathering.GameSnapshotJson))?.GetColumnType());
    }

    [Fact]
    public void Model_ConfiguresClubBggUsername_AsNullableMax100()
    {
        using var dbContext = CreateDbContext();

        var property = dbContext.Model.FindEntityType(typeof(Club))?
            .FindProperty(nameof(Club.BggUsername));

        Assert.NotNull(property);
        Assert.True(property.IsNullable);
        Assert.Equal(100, property.GetMaxLength());
        Assert.Contains(
            "20260828195607_ClubBggUsername",
            dbContext.Database.GetMigrations());
    }

    [Fact]
    public void Model_ConfiguresPersistentAdministrators()
    {
        using var dbContext = CreateDbContext();

        var entity = dbContext.Model.FindEntityType(typeof(OyinQAdministrator));

        Assert.NotNull(entity);
        Assert.Equal(
            Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never,
            entity.FindProperty(nameof(OyinQAdministrator.TelegramUserId))!.ValueGenerated);
        Assert.Contains(
            "20260828230514_PersistAdministrators",
            dbContext.Database.GetMigrations());
    }

    [Fact]
    public void StabilizationMigration_IsAdditiveAndMapsDurableWork()
    {
        using var dbContext = CreateDbContext();
        Assert.Contains("20260829000852_StabilizeClubCampMiniApp", dbContext.Database.GetMigrations());
        Assert.NotNull(dbContext.Model.FindEntityType(typeof(CampBggImport)));
        Assert.NotNull(dbContext.Model.FindEntityType(typeof(PendingTelegramPeerSelection)));
        Assert.Equal("jsonb", dbContext.Model.FindEntityType(typeof(CampBggImport))?
            .FindProperty(nameof(CampBggImport.DraftJson))?.GetColumnType());
        Assert.Equal(1L, dbContext.Model.FindEntityType(typeof(Club))?
            .FindProperty(nameof(Club.CollectionRevision))?.GetDefaultValue());
    }

    [Fact]
    public void GatheringCleanupMigration_MapsFocusedTelegramDeletionQueue()
    {
        using var dbContext = CreateDbContext();
        var cleanup = dbContext.Model.FindEntityType(typeof(TelegramMessageCleanup));

        Assert.NotNull(cleanup);
        Assert.Equal(2000, cleanup.FindProperty(nameof(TelegramMessageCleanup.LastError))?.GetMaxLength());
        Assert.Contains("20260830193242_GatheringHistoryAndCleanup", dbContext.Database.GetMigrations());
    }

    [Fact]
    public void CatalogMetadataMigrations_AreAdditiveAndMapped()
    {
        using var dbContext = CreateDbContext();
        var migrations = dbContext.Database.GetMigrations();

        Assert.Contains("20260830200452_CatalogMetadataCampAvailability", migrations);
        Assert.Contains("20260830201423_CampImportSkipResolution", migrations);
        Assert.Contains("20260830201708_ClubMetadataRefreshJobs", migrations);
        Assert.Equal(100, dbContext.Model.FindEntityType(typeof(CampRegistration))?
            .FindProperty(nameof(CampRegistration.City))?.GetMaxLength());
        Assert.NotNull(dbContext.Model.FindEntityType(typeof(CampGameContribution))?
            .FindProperty(nameof(CampGameContribution.Commitment)));
        Assert.NotNull(dbContext.Model.FindEntityType(typeof(ClubMetadataRefresh)));
    }

    [Fact]
    public void ExactCampAttendanceMigration_MapsCompositeDaysWithoutLegacyInference()
    {
        using var dbContext = CreateDbContext();
        var day = dbContext.Model.FindEntityType(typeof(CampRegistrationDay));

        Assert.Contains("20260831002206_ExactCampAttendanceDates", dbContext.Database.GetMigrations());
        Assert.Equal([nameof(CampRegistrationDay.CampRegistrationId), nameof(CampRegistrationDay.Date)],
            day!.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal(DeleteBehavior.Cascade, day.GetForeignKeys().Single().DeleteBehavior);
    }

    [Fact]
    public void CampRegistrationDisplayName_IsCampScoped()
    {
        using var dbContext = CreateDbContext();
        var property = dbContext.Model.FindEntityType(typeof(CampRegistration))?
            .FindProperty(nameof(CampRegistration.DisplayName));

        Assert.NotNull(property);
        Assert.True(property.IsNullable);
        Assert.Equal(128, property.GetMaxLength());
        Assert.Contains("20260831184750_SeparateCampAndParticipantDisplayNames",
            dbContext.Database.GetMigrations());
    }

    [Fact]
    public void ClubBggImports_ArePersistentAndHaveOneActiveJobPerClub()
    {
        using var dbContext = CreateDbContext();
        var entity = dbContext.Model.FindEntityType(typeof(ClubBggImport));

        Assert.NotNull(entity);
        Assert.Contains("20260831174110_ClubBggUsernameImports", dbContext.Database.GetMigrations());
        Assert.Equal(100, entity.FindProperty(nameof(ClubBggImport.BggUsername))?.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.GetFilter() == "\"Status\" IN (0, 1)"
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(ClubBggImport.ClubId)]));
    }

    [Fact]
    public void ChatScopedAdministration_IsAdditiveAndUniquelyScoped()
    {
        using var dbContext = CreateDbContext();
        var permission = dbContext.Model.FindEntityType(typeof(ChatAdminPermission));
        var knownChat = dbContext.Model.FindEntityType(typeof(KnownTelegramChat));

        Assert.NotNull(permission);
        Assert.NotNull(knownChat);
        Assert.Contains("20260901053106_ChatScopedAdministration", dbContext.Database.GetMigrations());
        Assert.Contains("20260901053721_TrackKnownTelegramChats", dbContext.Database.GetMigrations());
        Assert.Contains(permission!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(
                [nameof(ChatAdminPermission.TelegramUserId), nameof(ChatAdminPermission.CommunityKey)]));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=oyinq_model_test;Username=test;Password=test")
            .Options;

        return new AppDbContext(options);
    }
}
