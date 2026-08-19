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

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=oyinq_model_test;Username=test;Password=test")
            .Options;

        return new AppDbContext(options);
    }
}
