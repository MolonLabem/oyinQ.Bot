using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Data.Migrations;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Tests;

public sealed class ParticipantCollectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static CampBggImportDraftItem Item(long id, string name = "Игра") => new(id,
        CollectionItemType.BaseGame, null, new(CollectionItemSnapshot.CurrentVersion, name, null, null, 2, 4, null));
    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task ProfileImportConfirmationIsOwnedIdempotentAndIndependentOfCamp()
    {
        await using var db = CreateDb();
        db.Participants.Add(new Participant { Id = 1, TelegramUserId = 42, DisplayName = "Игрок" });
        var job = new CampBggImport { PublicId = Guid.NewGuid(), ParticipantId = 1, CampId = null,
            BggUsername = "owner", Status = CampBggImportStatus.Completed, ExpiresAt = Now.AddDays(1),
            DraftJson = CampBggImportDraftSerializer.Serialize(new(3, "owner", [Item(10), Item(20)])) };
        db.Add(job); await db.SaveChangesAsync();
        var coordinator = new CampBggImportCoordinator(db, Contributions(db),
            new CampParticipationPolicy(db, TimeProvider.System), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => coordinator.ConfirmAsync(job.PublicId, null, 2, [10], [], default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ConfirmAsync(job.PublicId, null, 1, [999], [], default));
        Assert.Empty(await db.ParticipantCollectionItems.ToArrayAsync());
        var first = await coordinator.ConfirmAsync(job.PublicId, null, 1, [10], [], default);
        var replay = await coordinator.ConfirmAsync(job.PublicId, null, 1, [10, 20], [], default);
        Assert.Equal(1, first.Added);
        Assert.Equal(first.Added, replay.Added);
        Assert.True(replay.WasAlreadyConfirmed);
        Assert.Equal(10, Assert.Single(await db.ParticipantCollectionItems.ToArrayAsync()).BggId);
        Assert.Empty(await db.CampGameContributions.ToArrayAsync());
    }

    [Theory]
    [InlineData(CampImportSkipReason.InvalidOrUnsupportedItem)]
    [InlineData(CampImportSkipReason.ProviderDataIncomplete)]
    public async Task ProfileImportCannotSelectRejectedProviderItems(CampImportSkipReason reason)
    {
        await using var db = CreateDb();
        db.Participants.Add(new Participant { Id = 1, TelegramUserId = 42, DisplayName = "Игрок" });
        var item = Item(10) with { SelectedByDefault = false, SkipReason = reason };
        var draft = new CampBggImportDraft(3, "owner", [item]);
        Assert.Equal(reason, Assert.Single(CampBggImportService.ClassifySkips(draft, new HashSet<long> { 10 }, new HashSet<(long, CollectionItemType)>()).Items).SkipReason);
        var job = new CampBggImport { ParticipantId = 1, BggUsername = "owner", Status = CampBggImportStatus.Completed,
            ExpiresAt = Now.AddDays(1), DraftJson = CampBggImportDraftSerializer.Serialize(draft) };
        db.Add(job); await db.SaveChangesAsync();
        var coordinator = new CampBggImportCoordinator(db, Contributions(db), new(db, TimeProvider.System), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ConfirmAsync(job.PublicId, null, 1, [10], [], default));
        Assert.Empty(db.ParticipantCollectionItems); Assert.Equal(CampBggImportStatus.Completed, job.Status);
    }

    [Fact]
    public async Task ImportsUpsertWithoutDeletingManualOwnershipOrCreatingCampPromises()
    {
        await using var db = CreateDb();
        db.Participants.Add(new Participant { Id = 1, TelegramUserId = 42, DisplayName = "Игрок" });
        await db.SaveChangesAsync();
        var service = new ParticipantCollectionService(db);
        await service.UpsertAsync(1, [Item(10), Item(20)], CollectionItemSource.Manual, Now, default);
        for (var i = 0; i < 2; i++)
            await service.UpsertAsync(1, [Item(20, "Обновлённое имя"), Item(30)], CollectionItemSource.BggImport, Now, default);
        Assert.Equal(3, await db.ParticipantCollectionItems.CountAsync());
        Assert.Equal(CollectionItemSource.Manual, (await db.ParticipantCollectionItems.SingleAsync(x => x.BggId == 20)).Source);
        Assert.Equal("Обновлённое имя", (await db.ParticipantCollectionItems.SingleAsync(x => x.BggId == 20)).ReadSnapshot().Name);
        Assert.Empty(await db.CampGameContributions.ToArrayAsync());
    }

    [Fact]
    public async Task ClubCatalogUnionsOnlyViewersCollectionAndDoesNotMutateClub()
    {
        await using var db = CreateDb();
        db.Participants.AddRange(new Participant { Id = 1, TelegramUserId = 42, DisplayName = "Первый" },
            new Participant { Id = 2, TelegramUserId = 43, DisplayName = "Второй" });
        var original = ClubCollectionSerializer.Serialize(new(ClubCollectionDocument.CurrentVersion,
            [Item(10).Snapshot.ToCollectionGame(10)]));
        db.Clubs.Add(new Club { BotChatKey = "club", Name = "Клуб", CollectionJson = original });
        await db.SaveChangesAsync();
        var collection = new ParticipantCollectionService(db);
        await collection.UpsertAsync(1, [Item(10), Item(20)], CollectionItemSource.Manual, Now, default);
        await collection.UpsertAsync(2, [Item(30)], CollectionItemSource.Manual, Now, default);
        var catalog = new GameCatalogService(db, new EffectiveCampCatalogService(db, Contributions(db)));
        var first = await catalog.ListAsync("club", Common.Options.BotMode.Club, 42, new(null, null, [], [], null), default);
        Assert.Equal(new long[] { 10, 20 }, first.Items.Select(x => x.BggId).Order());
        Assert.Equal("Есть в клубе · Есть у вас", first.Items.Single(x => x.BggId == 10).AvailabilitySummary);
        Assert.Equal("Есть у вас", first.Items.Single(x => x.BggId == 20).AvailabilitySummary);
        var second = await catalog.LoadClubAsync("club", 43, default);
        Assert.Equal(new long[] { 10, 30 }, second.Select(x => x.Game.BggId).Order());
        Assert.Equal(original, (await db.Clubs.SingleAsync()).CollectionJson);
    }

    [Fact]
    public async Task OwnershipSurvivesUnregistrationAndCanBeSelectedForAnotherCamp()
    {
        await using var db = CreateDb();
        db.Participants.Add(new Participant { Id = 1, TelegramUserId = 42, DisplayName = "Игрок" });
        await db.SaveChangesAsync();
        var first = await AddCamp(db, "first");
        var second = await AddCamp(db, "second");
        var ownership = new ParticipantCollectionService(db);
        await ownership.UpsertAsync(1, [Item(10)], CollectionItemSource.Manual, Now, default);
        var contributions = Contributions(db);
        await contributions.SetCommitmentAsync(first.Id, 1, 10, CollectionItemType.BaseGame, CampBringCommitment.Bringing, default);
        var catalog = new EffectiveCampCatalogService(db, contributions);
        Assert.Single(await catalog.LoadAsync("first", 1, default));
        Assert.Empty(await catalog.LoadAsync("second", 1, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ownership.RemoveAsync(1, 10, CollectionItemType.BaseGame, default));
        await new CampRegistrationService(db, TimeProvider.System).UnregisterAsync(first.Id, 1, default);
        Assert.Single(await db.ParticipantCollectionItems.ToArrayAsync());
        Assert.Empty(await db.CampGameContributions.ToArrayAsync());
        await contributions.SetCommitmentAsync(second.Id, 1, 10, CollectionItemType.BaseGame, CampBringCommitment.Available, default);
        var promise = Assert.Single(await db.CampGameContributions.ToArrayAsync());
        Assert.Equal(second.Id, promise.CampId);
        Assert.Equal(CampBringCommitment.Available, promise.Commitment);
        Assert.Single(await db.ParticipantCollectionItems.ToArrayAsync());
    }

    [Fact]
    public async Task CollectionDoesNotGrantCampRegistrationOrLetAnotherUserOfferIt()
    {
        await using var db = CreateDb();
        db.Participants.AddRange(new Participant { Id = 1, TelegramUserId = 42, DisplayName = "Игрок" },
            new Participant { Id = 2, TelegramUserId = 43, DisplayName = "Другой" });
        await db.SaveChangesAsync();
        var camp = await AddCamp(db, "camp");
        await new ParticipantCollectionService(db).UpsertAsync(2, [Item(10)], CollectionItemSource.Manual, Now, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Contributions(db).SetCommitmentAsync(camp.Id,
            2, 10, CollectionItemType.BaseGame, CampBringCommitment.Available, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Contributions(db).SetCommitmentAsync(camp.Id,
            1, 10, CollectionItemType.BaseGame, CampBringCommitment.Available, default));
        Assert.Empty(await db.CampGameContributions.ToArrayAsync());
    }

    [Fact]
    public void MigrationBackfillsKnownIdentityWithoutUpdatingEventOrHistoricalRows()
    {
        Migration migration = new PersistentParticipantCollection();
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        Assert.Contains("DISTINCT ON (\"ParticipantId\", \"BggId\", \"ItemType\")", sql);
        Assert.Contains("WHERE \"BggId\" > 0", sql);
        Assert.Contains("ON CONFLICT (\"ParticipantId\", \"BggId\", \"ItemType\") DO NOTHING", sql);
        Assert.DoesNotContain("UPDATE", sql);
        Assert.DoesNotContain("DELETE", sql);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DeleteDataOperation or UpdateDataOperation);
        var created = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("ParticipantCollectionItems", created.Name);
        Assert.DoesNotContain(created.Columns, x => x.Name is "CampId" or "CommunityKey" or "Commitment");
    }

    private static CampContributionSelectionService Contributions(AppDbContext db) =>
        new(db, new CampParticipationPolicy(db, TimeProvider.System), TimeProvider.System);

    private static async Task<Camp> AddCamp(AppDbContext db, string key)
    {
        var start = DateOnly.FromDateTime(Now.UtcDateTime);
        var community = new OyinQCommunity { Key = key, Name = key, Mode = Common.Options.BotMode.Camp,
            TimeZoneId = "UTC", IsActive = true };
        var camp = new Camp { Name = key, BotChatKey = key, BotChat = community, Status = CampStatus.Active,
            StartsAtUtc = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), EndsAtUtc = new DateTimeOffset(start.AddDays(4).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) };
        db.Add(camp); await db.SaveChangesAsync();
        db.CampRegistrations.Add(new CampRegistration { CampId = camp.Id, ParticipantId = 1,
            City = "Алматы", NeedsAccommodation = false, SelectedDays = [new() { Date = start }] });
        await db.SaveChangesAsync(); return camp;
    }
}
