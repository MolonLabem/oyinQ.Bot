using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;

namespace oyinQ.Bot.Tests;

public sealed class CampWishlistTests
{
    [Fact]
    public async Task PrivateOwnershipSuggestsOnlyToOwnerAndOldWishesStayAnonymous()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        var service = s.Service(f.Db, f.Clock);
        var mine = await service.ListAsync("camp-wishes", s.Owner.Id, new("bring"), default);
        Assert.Equal(42, Assert.Single(mine.Items).Game.BggId); Assert.Equal(1, mine.Suggestions);
        var visitor = await service.DetailsAsync("camp-wishes", s.A.Id, 42, default);
        Assert.Empty(visitor.Owners); Assert.False(visitor.Item.Confirmed);
        var ownerView = await service.DetailsAsync("camp-wishes", s.Owner.Id, 42, default);
        Assert.Empty(ownerView.Interested); Assert.Equal(1, ownerView.AnonymousCount);
        Assert.Empty(f.Db.Notifications);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Act(f.Db, f.Clock, s.A.Id, "request", s.Owner.PublicId));
    }

    [Fact]
    public async Task OfferAndConfirmationUseExistingContributionAndCoalescePendingMessages()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "offer");
        var offered = Assert.Single(f.Db.Notifications); Assert.Equal(NotificationKind.CampBoxOffered, offered.Kind);
        Assert.True(await new CampWishlistNotifications(f.Db, f.Clock).PrepareAsync(offered, default));
        Assert.Contains("Пока без окончательного подтверждения", offered.Text);
        Assert.False((await s.Service(f.Db, f.Clock).DetailsAsync("camp-wishes", s.A.Id, 42, default)).Item.Confirmed);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "confirm");
        Assert.Equal(NotificationState.Expired, offered.State);
        var confirmed = Assert.Single(f.Db.Notifications.Where(x => x.Kind == NotificationKind.CampBoxConfirmed));
        Assert.True(await new CampWishlistNotifications(f.Db, f.Clock).PrepareAsync(confirmed, default));
        Assert.Contains("нашлась коробка", confirmed.Text);
        Assert.Equal(CampBringCommitment.Bringing, Assert.Single(f.Db.CampGameContributions).Commitment);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "offer"); await s.Act(f.Db, f.Clock, s.Owner.Id, "confirm");
        Assert.Equal(2, await f.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task DeliveredOfferThenConfirmationHaveDifferentTruthfulMessages()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        s.A.PrivateChatStartedAt = f.Clock.Now; await f.Db.SaveChangesAsync(); var transport = new Capture();
        await s.Act(f.Db, f.Clock, s.Owner.Id, "offer");
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "confirm");
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(2, transport.Texts.Count); Assert.Contains("может привезти", transport.Texts[0]); Assert.Contains("привезёт", transport.Texts[1]);
    }

    [Fact]
    public async Task RequestsMergeCancelAndRefusalCannotBeBypassed()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "privacy", share: true);
        await s.Act(f.Db, f.Clock, s.A.Id, "request", s.Owner.PublicId);
        await s.Act(f.Db, f.Clock, s.A.Id, "request", s.Owner.PublicId);
        await s.Act(f.Db, f.Clock, s.B.Id, "request", s.Owner.PublicId);
        var request = Assert.Single(f.Db.CampBringRequests); Assert.Equal(2, request.Requesters.Count);
        Assert.Single(f.Db.Notifications.Where(x => x.Kind == NotificationKind.CampBringRequested));
        Assert.Single(f.Db.GameWishes); Assert.Empty(f.Db.CampGameContributions);
        await s.Act(f.Db, f.Clock, s.A.Id, "cancel", s.Owner.PublicId);
        Assert.False(request.Requesters.Single(x => x.ParticipantId == s.A.Id).Active);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "decline");
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Act(f.Db, f.Clock, s.A.Id, "request", s.Owner.PublicId));
        Assert.Equal("declined", Assert.Single(await s.Service(f.Db, f.Clock).IncomingAsync("camp-wishes", s.Owner.Id, default)).State);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "confirm");
        Assert.False(request.Declined);
        Assert.Single(f.Db.Notifications.Where(x => x.Kind == NotificationKind.CampBoxConfirmed && x.ParticipantId == s.B.Id));
        Assert.Single(f.Db.Notifications.Where(x => x.Kind == NotificationKind.CampBoxConfirmed && x.ParticipantId == s.A.Id));
    }

    [Fact]
    public async Task SpecificDatesAndWithdrawalKeepExistingGatheringAndOtherBox()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        var game = new GameGathering { Community = s.Camp.BotChat, OrganizerParticipant = s.A, PublicId = Guid.NewGuid(),
            StartsAtUtc = f.Clock.Now.AddHours(2), MinimumPlayers = 1, DesiredPlayers = 2, MaximumPlayers = 4,
            GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new(GatheringGameSnapshot.CurrentVersion, 42, "Немезида", null, null, 1, 5, null, [], "demand", [])) };
        f.Db.GameGatherings.Add(game); await f.Db.SaveChangesAsync();
        var policy = new CampParticipationPolicy(f.Db, f.Clock); var contribution = new CampContributionSelectionService(f.Db, policy, f.Clock);
        var catalog = new GameCatalogService(f.Db, new(f.Db, contribution), f.Clock);
        var providers = new GameProviderService(f.Db, catalog, new(f.Db, contribution), policy, contribution, f.Clock);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "confirm", dates: [s.Day.AddDays(1)]);
        Assert.False((await providers.ForGatheringAsync(game, s.A.Id, default)).IsConfirmed);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "confirm", dates: [s.Day]);
        Assert.True((await providers.ForGatheringAsync(game, s.A.Id, default)).IsConfirmed);
        f.Db.ParticipantCollectionItems.Add(CampWishSeed.Owned(s.B, 42)); await f.Db.SaveChangesAsync();
        await s.Act(f.Db, f.Clock, s.B.Id, "confirm", dates: [s.Day]);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "withdraw");
        Assert.True((await providers.ForGatheringAsync(game, s.A.Id, default)).IsConfirmed);
        Assert.Equal(game.PublicId, Assert.Single(f.Db.GameGatherings).PublicId);
    }

    [Fact]
    public async Task AnotherOwnersBoxResolvesRequestOnlyWhenAllRequestedDaysCovered()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "privacy", share: true);
        await s.Act(f.Db, f.Clock, s.A.Id, "request", s.Owner.PublicId);
        f.Db.ParticipantCollectionItems.Add(CampWishSeed.Owned(s.B, 42)); await f.Db.SaveChangesAsync();
        await s.Act(f.Db, f.Clock, s.B.Id, "confirm", dates: [s.Day]);
        Assert.Equal("pending", Assert.Single(await s.Service(f.Db, f.Clock).IncomingAsync("camp-wishes", s.Owner.Id, default)).State);
        await s.Act(f.Db, f.Clock, s.B.Id, "confirm", dates: [s.Day, s.Day.AddDays(1)]);
        Assert.Equal("found", Assert.Single(await s.Service(f.Db, f.Clock).IncomingAsync("camp-wishes", s.Owner.Id, default)).State);
        Assert.DoesNotContain(f.Db.CampGameContributions, x => x.ParticipantId == s.Owner.Id);
    }

    [Fact]
    public async Task ReadsNeverNotifyAndSearchCountsWholeDatasetBeforePaging()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        for (var i = 100; i < 145; i++) f.Db.GameWishes.Add(CampWishSeed.Wish(s.Camp, s.A, i, $"Игра {i}", $"Alias {i}"));
        await f.Db.SaveChangesAsync(); var service = s.Service(f.Db, f.Clock);
        var all = await service.ListAsync("camp-wishes", s.Owner.Id, new(), default); Assert.Equal(46, all.Total); Assert.Equal(20, all.Items.Count); Assert.True(all.HasMore);
        var match = await service.ListAsync("camp-wishes", s.Owner.Id, new(Search: "Alias 144"), default); Assert.Equal(144, Assert.Single(match.Items).Game.BggId);
        await service.ProfileAsync("camp-wishes", s.A.Id, s.Owner.PublicId, null, "games", 1, default);
        await service.IncomingAsync("camp-wishes", s.Owner.Id, default);
        Assert.Empty(f.Db.Notifications);
    }

    [Fact]
    public async Task PreferencesPrivateDeliveryAndStaleRegistrationAreRechecked()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "confirm");
        var transport = new Capture(); await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Empty(transport.Texts); Assert.Equal(NotificationState.CannotMessageUser, Assert.Single(f.Db.Notifications).State);
        var registration = await f.Db.CampRegistrations.SingleAsync(x => x.ParticipantId == s.Owner.Id);
        f.Db.CampRegistrationDays.RemoveRange(registration.SelectedDays); registration.SelectedDays.Clear();
        s.A.PrivateChatStartedAt = f.Clock.Now.AddMinutes(1); await f.Db.SaveChangesAsync();
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(NotificationState.Expired, Assert.Single(f.Db.Notifications).State); Assert.Empty(transport.Texts);
        Assert.False(NotificationPolicy.Allows(NotificationKind.CampBoxConfirmed, new() { WishlistGathering = false }));
    }

    [Fact]
    public async Task OwnWishDoesNotCreateRecommendationAndOtherCampCannotReadProfiles()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        f.Db.GameWishes.RemoveRange(f.Db.GameWishes); f.Db.GameWishes.Add(CampWishSeed.Wish(s.Camp, s.Owner, 42, "Немезида", "Nemesis")); await f.Db.SaveChangesAsync();
        Assert.Empty((await s.Service(f.Db, f.Clock).ListAsync("camp-wishes", s.Owner.Id, new("bring"), default)).Items);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Service(f.Db, f.Clock).ListAsync("camp-wishes", f.Me.Id, new(), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => s.Service(f.Db, f.Clock).ProfileAsync("camp-wishes", s.A.Id, f.Me.PublicId, null, null, 1, default));
    }

    [Fact]
    public async Task ConsentIsScopedAndRevokingCollectionDoesNotEraseAnExistingReply()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        var service = s.Service(f.Db, f.Clock);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "privacy", share: true);
        Assert.Single((await service.DetailsAsync("camp-wishes", s.A.Id, 42, default)).Owners);
        await s.Act(f.Db, f.Clock, s.A.Id, "request", s.Owner.PublicId);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "privacy", share: false);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "decline");
        var details = await service.DetailsAsync("camp-wishes", s.A.Id, 42, default);
        Assert.Empty(details.Owners); Assert.Equal("declined", Assert.Single(details.Requests).State);
        await service.ActAsync("camp-wishes", s.A.Id, 0, "privacy", null, null, null, true, default);
        Assert.Single((await service.DetailsAsync("camp-wishes", s.Owner.Id, 42, default)).Interested);
        Assert.Single(f.Db.GameWishes);
    }

    [Fact]
    public async Task NonOverlappingAttendanceDoesNotSuggestOrNotifyAndClosedCampRejectsActions()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        var registration = await f.Db.CampRegistrations.SingleAsync(x => x.ParticipantId == s.Owner.Id);
        f.Db.CampRegistrationDays.RemoveRange(registration.SelectedDays); registration.SelectedDays.Clear();
        registration.SelectedDays.Add(new() { Date = s.Day.AddDays(2) }); await f.Db.SaveChangesAsync();
        Assert.Empty((await s.Service(f.Db, f.Clock).ListAsync("camp-wishes", s.Owner.Id, new("bring"), default)).Items);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "confirm"); Assert.Empty(f.Db.Notifications);
        Assert.False((await s.Service(f.Db, f.Clock).DetailsAsync("camp-wishes", s.A.Id, 42, default)).Item.Confirmed);
        s.Camp.Status = CampStatus.Closed; await f.Db.SaveChangesAsync();
        Assert.False((await s.Service(f.Db, f.Clock).ListAsync("camp-wishes", s.A.Id, new(), default)).CanAct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Act(f.Db, f.Clock, s.Owner.Id, "offer"));
    }

    [Fact]
    public async Task PersistedDailyLimitPreventsMassRequestsAndDoesNotBlockIdempotentRetry()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "privacy", share: true);
        for (var i = 100; i < 111; i++) f.Db.ParticipantCollectionItems.Add(CampWishSeed.Owned(s.Owner, i));
        await f.Db.SaveChangesAsync();
        for (var i = 100; i < 110; i++) await s.Service(f.Db, f.Clock).ActAsync("camp-wishes", s.A.Id, i, "request", s.Owner.PublicId, null, null, null, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service(f.Db, f.Clock).ActAsync("camp-wishes", s.A.Id, 110, "request", s.Owner.PublicId, null, null, null, default));
        await s.Service(f.Db, f.Clock).ActAsync("camp-wishes", s.A.Id, 100, "request", s.Owner.PublicId, null, null, null, default);
        Assert.Equal(10, await f.Db.CampBringRequests.CountAsync());
        Assert.Equal(10, await f.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task CanonicalCommitmentClearsRefusalAndLateWishDoesNotReplayPastEvents()
    {
        await using var f = new PlanningFixture(); var s = await CampWishSeed.Create(f.Db, f.Clock);
        await s.Act(f.Db, f.Clock, s.Owner.Id, "decline");
        await new CampContributionSelectionService(f.Db, new(f.Db, f.Clock), f.Clock)
            .SetCommitmentAsync(s.Camp.Id, s.Owner.Id, 42, CollectionItemType.BaseGame, CampBringCommitment.Bringing, default);
        Assert.False(Assert.Single(f.Db.CampBringRequests).Declined);
        var before = await f.Db.Notifications.CountAsync();
        f.Db.GameWishes.Add(CampWishSeed.Wish(s.Camp, s.B, 42, "Немезида", "Nemesis")); await f.Db.SaveChangesAsync();
        Assert.True((await s.Service(f.Db, f.Clock).DetailsAsync("camp-wishes", s.B.Id, 42, default)).Item.Confirmed);
        await new CampContributionSelectionService(f.Db, new(f.Db, f.Clock), f.Clock)
            .SetCommitmentAsync(s.Camp.Id, s.Owner.Id, 42, CollectionItemType.BaseGame, CampBringCommitment.Bringing, default);
        Assert.Equal(before, await f.Db.Notifications.CountAsync());
    }

    private sealed class Capture : INotificationTransport
    {
        public List<string> Texts { get; } = [];
        public Task<NotificationReceipt> SendAsync(Notification n, Participant p, CancellationToken ct) { Texts.Add(n.Text); return Task.FromResult(new NotificationReceipt(Texts.Count)); }
    }
}

internal sealed record CampWishSeed(Camp Camp, Participant A, Participant B, Participant Owner, DateOnly Day)
{
    public CampWishlistService Service(AppDbContext db, TimeProvider clock) => new(db, new(db, new(db, clock), clock), clock);
    public Task Act(AppDbContext db, TimeProvider clock, long actor, string action, Guid? owner = null, DateOnly[]? dates = null, bool? share = null) =>
        Service(db, clock).ActAsync("camp-wishes", actor, 42, action, owner, dates, share, null, default);
    public static ParticipantCollectionItem Owned(Participant p, long game) => new() { Participant = p, BggId = game, ItemType = CollectionItemType.BaseGame,
        SnapshotJson = CollectionItemSnapshotSerializer.Serialize(new(1, "Немезида", null, null, 1, 5, null, OriginalName: "Nemesis")) };
    public static GameWish Wish(Camp camp, Participant p, long id, string name, string original) => new() { Community = camp.BotChat, Participant = p, BggId = id,
        SnapshotJson = ClubCollectionSerializer.Serialize(new(2, [new(id, name, null, null, 1, 5, null, [], OriginalName: original)])) };
    public static async Task<CampWishSeed> Create(AppDbContext db, TimeProvider clock)
    {
        await db.SaveChangesAsync();
        var day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var camp = new Camp { Name = "Осенний кэмп", BotChat = new() { Key = "camp-wishes", Name = "Осенний кэмп", Mode = BotMode.Camp, TelegramChatId = -10012345678, TimeZoneId = "UTC", IsActive = true },
            Status = CampStatus.Active, StartsAtUtc = clock.GetUtcNow().AddHours(-1), EndsAtUtc = clock.GetUtcNow().AddDays(3) };
        var a = new Participant { TelegramUserId = 81001, PublicId = Guid.NewGuid(), DisplayName = "Алексей" };
        var b = new Participant { TelegramUserId = 81002, PublicId = Guid.NewGuid(), DisplayName = "Борис" };
        var owner = new Participant { TelegramUserId = 81003, PublicId = Guid.NewGuid(), DisplayName = "Виктор" };
        foreach (var p in new[] { a, b, owner }) db.CampRegistrations.Add(new() { Camp = camp, Participant = p, City = "Алматы", NeedsAccommodation = false,
            SelectedDays = [new() { Date = day }, new() { Date = day.AddDays(1) }] });
        db.ParticipantCollectionItems.Add(Owned(owner, 42)); db.GameWishes.Add(Wish(camp, a, 42, "Немезида", "Nemesis"));
        await db.SaveChangesAsync(); return new(camp, a, b, owner, day);
    }
}

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task ConcurrentCampRequestsAndRestartKeepSingleRequestAndNotification()
    {
        await using var database = await Database.CreateAsync();
        CampWishSeed seed;
        await using (var db = database.Open()) { seed = await CampWishSeed.Create(db, Time); await seed.Act(db, Time, seed.Owner.Id, "privacy", share: true); }
        async Task Request(long actor) { await using var db = database.Open(); await seed.Act(db, Time, actor, "request", seed.Owner.PublicId); }
        await Task.WhenAll(Request(seed.A.Id), Request(seed.A.Id), Request(seed.B.Id));
        await Request(seed.A.Id);
        await using var check = database.Open();
        Assert.Single(await check.CampBringRequests.ToArrayAsync()); Assert.Equal(2, await check.CampBringRequesters.CountAsync());
        Assert.Single(await check.Notifications.ToArrayAsync());
        Assert.False(check.Database.HasPendingModelChanges());
        await seed.Act(check, Time, seed.Owner.Id, "confirm");
        Assert.Equal(2, await check.Notifications.CountAsync(x => x.Kind == NotificationKind.CampBoxConfirmed));
    }
}
