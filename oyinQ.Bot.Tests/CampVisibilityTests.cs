using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Notifications;

namespace oyinQ.Bot.Tests;

public sealed class CampVisibilityTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static GameCatalogService Catalog(AppDbContext db, TimeProvider clock) => new(db, new(db, new(db, new(db, clock), clock)), clock);

    [Fact]
    public async Task NoSettingsOpensBothDirectionsWithoutMakingBoxesAvailableOrSendingAnything()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        f.Db.ParticipantCollectionItems.Add(CampWishSeed.Owned(s.A, 99));
        f.Db.GameWishes.Add(CampWishSeed.Wish(s.Camp, s.Owner, 99, "Другая игра", "Another game"));
        await f.Db.SaveChangesAsync();
        var service = s.Service(f.Db, f.Clock);
        foreach (var (visitor, owner, game) in new[] { (s.A, s.Owner, 42L), (s.Owner, s.A, 99L) })
        {
            var settings = await service.SettingsAsync("camp-wishes", visitor.Id, default);
            var list = await service.ListAsync("camp-wishes", visitor.Id, new(), default);
            Assert.True(settings.ShareCollection); Assert.True(settings.ShareWishes);
            Assert.True(list.ShareCollection); Assert.True(list.ShareWishes);
            var detail = await service.DetailsAsync("camp-wishes", visitor.Id, game, default);
            Assert.Equal(owner.PublicId, Assert.Single(detail.Owners).Person.Id);
            Assert.Equal("owned", detail.Owners[0].Status); Assert.False(detail.Item.Confirmed);
            var profile = Json(await service.ProfileAsync("camp-wishes", visitor.Id, owner.PublicId, null, "games", 1, default));
            Assert.Equal(game, Assert.Single(profile.GetProperty("Items").EnumerateArray()).GetProperty("Game").GetProperty("BggId").GetInt64());
            var wishProfile = Json(await service.ProfileAsync("camp-wishes", owner.Id, visitor.PublicId, null, "wishes", 1, default));
            Assert.True(wishProfile.GetProperty("WishesVisible").GetBoolean());
            Assert.Equal(1, wishProfile.GetProperty("Total").GetInt32());
            Assert.Equal(visitor.PublicId, Assert.Single((await service.DetailsAsync("camp-wishes", owner.Id, game, default)).Interested).Id);
            Assert.Equal(game, (await Catalog(f.Db, f.Clock).DemandGameAsync("camp-wishes", BotMode.Camp, visitor.TelegramUserId, game, default)).BggId);
        }
        Assert.Empty(f.Db.CampParticipantVisibilities); Assert.Empty(f.Db.CampGameContributions);
        Assert.Empty(f.Db.CampBringRequests); Assert.Empty(f.Db.GameGatherings); Assert.Empty(f.Db.Notifications);
        Assert.Equal(2, await f.Db.ParticipantCollectionItems.CountAsync()); Assert.Equal(2, await f.Db.GameWishes.CountAsync());
    }

    [Fact]
    public async Task HidingAppliesToDirectDetailsProfilesCatalogAndRequestsWhileOffersStayVisible()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        f.Db.ParticipantCollectionItems.Add(CampWishSeed.Owned(s.Owner, 99)); await f.Db.SaveChangesAsync();
        var service = s.Service(f.Db, f.Clock); var catalog = Catalog(f.Db, f.Clock);
        // This game has no wish or contribution: the demand picker must use the same visibility rule.
        Assert.Equal(99, (await catalog.DemandGameAsync("camp-wishes", BotMode.Camp, s.A.TelegramUserId, 99, default)).BggId);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "privacy", share: false);
        await service.ActAsync("camp-wishes", s.A.Id, 0, "privacy", null, null, null, false, default);
        Assert.Empty((await service.DetailsAsync("camp-wishes", s.A.Id, 42, default)).Owners);
        var anonymous = await service.DetailsAsync("camp-wishes", s.Owner.Id, 42, default);
        Assert.Empty(anonymous.Interested); Assert.Equal(1, anonymous.AnonymousCount);
        Assert.Equal(1, anonymous.Item.InterestedParticipants);
        Assert.Equal(0, Json(await service.ProfileAsync("camp-wishes", s.A.Id, s.Owner.PublicId, null, "games", 1, default)).GetProperty("Total").GetInt32());
        var wishes = Json(await service.ProfileAsync("camp-wishes", s.Owner.Id, s.A.PublicId, null, "wishes", 1, default));
        Assert.False(wishes.GetProperty("WishesVisible").GetBoolean()); Assert.Equal(0, wishes.GetProperty("Total").GetInt32());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DetailsAsync("camp-wishes", s.A.Id, 99, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => catalog.DemandGameAsync("camp-wishes", BotMode.Camp, s.A.TelegramUserId, 99, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Act(f.Db, f.Clock, s.A.Id, "request", s.Owner.PublicId));
        Assert.Empty(f.Db.Notifications); Assert.Empty(f.Db.CampGameContributions);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "offer", dates: [s.Day]);
        var offered = Assert.Single((await service.DetailsAsync("camp-wishes", s.A.Id, 42, default)).Owners);
        Assert.Equal("Available", offered.Status); Assert.Equal([s.Day], offered.Dates);
        Assert.Equal(1, Json(await service.ProfileAsync("camp-wishes", s.A.Id, s.Owner.PublicId, null, "games", 1, default)).GetProperty("Total").GetInt32());
        Assert.False((await service.DetailsAsync("camp-wishes", s.A.Id, 42, default)).Item.Confirmed);
    }

    [Fact]
    public async Task ExplicitChoiceSurvivesRegistrationEditsExitRejoinAndCollectionImports()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        await s.Service(f.Db, f.Clock).ActAsync("camp-wishes", s.Owner.Id, 0, "privacy", null, null, false, false, default);
        var registrations = new CampRegistrationService(f.Db, f.Clock);
        await registrations.SaveAsync(s.Camp.Id, s.Owner.Id, [s.Day], false, null, "Алматы", false, default);
        await registrations.UnregisterAsync(s.Camp.Id, s.Owner.Id, default);
        Assert.False((await f.Db.CampParticipantVisibilities.SingleAsync()).ShareCollection);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => s.Service(f.Db, f.Clock).ProfileAsync("camp-wishes", s.A.Id, s.Owner.PublicId, null, null, 1, default));
        await registrations.SaveAsync(s.Camp.Id, s.Owner.Id, [s.Day], false, null, "Алматы", false, default);
        await new Features.Collections.ParticipantCollectionService(f.Db).UpsertAsync(s.Owner.Id,
            [new(99, Data.Entities.CollectionItemType.BaseGame, null, new(1, "Импорт", null, null, 1, 4, null))],
            Data.Entities.CollectionItemSource.BggImport, f.Clock.GetUtcNow(), default);
        f.Db.ChangeTracker.Clear();
        var settings = await s.Service(f.Db, f.Clock).SettingsAsync("camp-wishes", s.Owner.Id, default);
        Assert.False(settings.ShareCollection); Assert.False(settings.ShareWishes);
        Assert.Equal([s.Day], settings.MyDates);
        Assert.Equal(2, await f.Db.ParticipantCollectionItems.CountAsync());
        Assert.Empty(f.Db.Notifications); Assert.Empty(f.Db.CampGameContributions);
    }

    [Fact]
    public async Task PendingRequestChecksCurrentDefaultAndExplicitHidingWithoutCreatingNewNotices()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        await s.Act(f.Db, f.Clock, s.A.Id, "request", s.Owner.PublicId);
        var notice = Assert.Single(f.Db.Notifications);
        var notifications = new CampWishlistNotifications(f.Db, f.Clock);
        Assert.True(await notifications.PrepareAsync(notice, default));
        await s.Act(f.Db, f.Clock, s.Owner.Id, "privacy", share: false);
        Assert.False(await notifications.PrepareAsync(notice, default));
        await s.Act(f.Db, f.Clock, s.Owner.Id, "privacy", share: true);
        Assert.Single(f.Db.Notifications); Assert.Empty(f.Db.CampGameContributions);
    }

    [Fact]
    public async Task VisibilityChoiceAndWishAuthorsStayWithinTheirCampAndRequireCurrentRegistrations()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        var second = new Data.Entities.Camp { Name = "Другой кэмп", Status = Data.Entities.CampStatus.Active,
            StartsAtUtc = s.Camp.StartsAtUtc, EndsAtUtc = s.Camp.EndsAtUtc,
            BotChat = new() { Key = "second-camp", Name = "Другой кэмп", Mode = BotMode.Camp, IsActive = true, TimeZoneId = "UTC" } };
        foreach (var participant in new[] { s.A, s.Owner }) f.Db.CampRegistrations.Add(new() { Camp = second,
            Participant = participant, City = "Алматы", NeedsAccommodation = false, SelectedDays = [new() { Date = s.Day }] });
        f.Db.GameWishes.Add(CampWishSeed.Wish(second, s.Owner, 100, "Другая хотелка", "Other wish"));
        await f.Db.SaveChangesAsync();
        await s.Service(f.Db, f.Clock).ActAsync("camp-wishes", s.Owner.Id, 0, "privacy", null, null, false, false, default);
        var service = s.Service(f.Db, f.Clock);
        Assert.Equal(42, Assert.Single((await service.ListAsync("camp-wishes", s.A.Id, new(), default)).Items).Game.BggId);
        Assert.True((await service.SettingsAsync("second-camp", s.Owner.Id, default)).ShareCollection);
        Assert.Equal(s.Owner.PublicId, Assert.Single((await service.DetailsAsync("second-camp", s.A.Id, 42, default)).Owners).Person.Id);
        var wishes = Json(await service.ProfileAsync("second-camp", s.A.Id, s.Owner.PublicId, null, "wishes", 1, default));
        Assert.Equal(100, Assert.Single(wishes.GetProperty("Items").EnumerateArray()).GetProperty("Game").GetProperty("BggId").GetInt64());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync("second-camp", s.B.Id, new(), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ProfileAsync("second-camp", s.A.Id, s.B.PublicId, null, null, 1, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DetailsAsync("camp-wishes", s.A.Id, 100, default));
        var registration = await f.Db.CampRegistrations.Include(x => x.SelectedDays).SingleAsync(x => x.CampId == second.Id && x.ParticipantId == s.Owner.Id);
        registration.SelectedDays.Clear(); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ProfileAsync("second-camp", s.A.Id, s.Owner.PublicId, null, null, 1, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DetailsAsync("second-camp", s.A.Id, 42, default));
        Assert.Empty(f.Db.Notifications); Assert.Empty(f.Db.CampGameContributions);
    }
}

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task VisibilityMigrationPreservesEveryLegacyFlagAndDoesNotReapplyAfterNewChoices()
    {
        await using var database = await Database.CreateAsync("20260923134044_CampWishlistFlow");
        CampWishSeed seed;
        await using (var db = database.Open())
        {
            seed = await CampWishSeed.Create(db, Time);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CampRegistrations\" SET \"ShareCollection\" = TRUE WHERE \"ParticipantId\" = {seed.A.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CampRegistrations\" SET \"ShareWishes\" = TRUE WHERE \"ParticipantId\" = {seed.B.Id}");
            await db.Database.MigrateAsync();
            var settings = await db.CampParticipantVisibilities.ToDictionaryAsync(x => x.ParticipantId);
            Assert.True(settings[seed.A.Id].ShareCollection); Assert.False(settings[seed.A.Id].ShareWishes);
            Assert.False(settings[seed.B.Id].ShareCollection); Assert.True(settings[seed.B.Id].ShareWishes);
            Assert.False(settings[seed.Owner.Id].ShareCollection); Assert.False(settings[seed.Owner.Id].ShareWishes);
            await seed.Act(db, Time, seed.Owner.Id, "privacy", share: true);
        }
        await using var restarted = database.Open();
        await restarted.Database.MigrateAsync();
        Assert.True((await seed.Service(restarted, Time).SettingsAsync("camp-wishes", seed.Owner.Id, default)).ShareCollection);
        Assert.False(restarted.Database.HasPendingModelChanges());
    }
}
