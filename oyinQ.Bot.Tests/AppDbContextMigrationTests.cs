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

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=oyinq_model_test;Username=test;Password=test")
            .Options;

        return new AppDbContext(options);
    }
}
