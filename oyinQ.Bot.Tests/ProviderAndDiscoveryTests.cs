using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;

namespace oyinQ.Bot.Tests;

public sealed class ProviderAndDiscoveryTests
{
    [Fact]
    public void ProviderProjection_DeduplicatesPeopleAndKeepsTheirStrongestCommitment()
    {
        CampCatalogProvider possible = new(7, "Игрок", null, CollectionItemSource.Manual, CampBringCommitment.Available, false, null);
        var state = GameProviderService.Describe(false, [possible, possible with { Commitment = CampBringCommitment.Bringing }]);
        Assert.Equal(GameProviderState.ConfirmedParticipantProvider, state.State);
        Assert.Equal(CampBringCommitment.Bringing, Assert.Single(state.Providers).Commitment);
    }

    [Fact]
    public async Task PersistentOwnership_IsNotBringing_AndOneTapCommitmentIsCampScoped()
    {
        await using var f = new PlanningFixture();
        var camp = AddCamp(f, "camp"); var otherCamp = AddCamp(f, "other-camp");
        var registration = Register(f, camp, f.Me, f.Clock.Now);
        AddOwned(f, f.Me, 42); AddOwned(f, f.Other, 42); await f.Db.SaveChangesAsync();
        var (catalog, providers, _) = Services(f);
        var before = await providers.ForGameAsync("camp", 42, f.Me.Id, f.Clock.Now.AddHours(1), default);
        Assert.Equal(GameProviderState.NoKnownProvider, before.State); Assert.True(before.CanBring);
        Assert.False((await providers.ForGameAsync("camp", 42, f.Other.Id, f.Clock.Now.AddHours(1), default)).CanBring);
        var g = new GameGathering { PublicId = Guid.NewGuid(), Community = camp.BotChat, StartsAtUtc = f.Clock.Now.AddHours(1),
            OrganizerParticipant = f.Me, Status = GatheringStatus.Recruiting, MinimumPlayers = 1, DesiredPlayers = 2, MaximumPlayers = 3,
            GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new(GatheringGameSnapshot.CurrentVersion, 42, "Игра", null, null, 1, 8, null, [], "catalog", [])) };
        f.Db.GameGatherings.Add(g); await f.Db.SaveChangesAsync();
        await providers.BringAsync(g, f.Me.Id, default); await providers.ForGatheringAsync(g, f.Me.Id, default);
        var contribution = Assert.Single(f.Db.CampGameContributions);
        Assert.Equal(camp.Id, contribution.CampId); Assert.Equal(CampBringCommitment.Bringing, contribution.Commitment);
        Assert.Equal(GameProviderState.ConfirmedParticipantProvider, (await providers.ForGatheringAsync(g, f.Me.Id, default)).State);
        Assert.Empty((await catalog.ListAsync("other-camp", BotMode.Camp, f.Me.TelegramUserId, new(null, null, [], [], null), default)).Items);
        var nextDay = await providers.ForGameAsync("camp", 42, f.Me.Id, g.StartsAtUtc.AddDays(1), default);
        Assert.Equal(GameProviderState.NoKnownProvider, nextDay.State); Assert.False(nextDay.CanBring);
        Assert.Single(registration.SelectedDays); Assert.NotEqual(camp.Id, otherCamp.Id);
    }

    [Fact]
    public async Task AvailableIsOnlyPossibleProvider_AndMissingProviderNoticeIsDeduplicated()
    {
        await using var f = new PlanningFixture(); var camp = AddCamp(f, "camp"); Register(f, camp, f.Me, f.Clock.Now); AddOwned(f, f.Me, 42);
        var g = new GameGathering { PublicId = Guid.NewGuid(), Community = camp.BotChat, OrganizerParticipant = f.Me, StartsAtUtc = f.Clock.Now.AddHours(1),
            Status = GatheringStatus.Recruiting, MinimumPlayers = 1, DesiredPlayers = 2, MaximumPlayers = 3,
            GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new(GatheringGameSnapshot.CurrentVersion, 42, "Игра", null, null, 1, 8, null, [], "catalog", [])) };
        f.Db.GameGatherings.Add(g); f.Db.NotificationPreferences.Add(new() { Participant = f.Me, OrganizerMissingProvider = true }); await f.Db.SaveChangesAsync();
        var (_, providers, contributions) = Services(f);
        await contributions.SetCommitmentAsync(camp.Id, f.Me.Id, 42, CollectionItemType.BaseGame, CampBringCommitment.Available, default);
        var state = await providers.ForGatheringAsync(g, f.Me.Id, default);
        Assert.Equal(GameProviderState.AvailableParticipantProviders, state.State); Assert.False(state.IsConfirmed);
        var attention = new ProviderAttentionService(f.Db, providers, new NotificationService(f.Db, f.Clock), f.Clock);
        await attention.EnqueueDueAsync(default); await attention.EnqueueDueAsync(default);
        Assert.Equal(NotificationKind.OrganizerMissingProvider, Assert.Single(f.Db.Notifications).Kind);
        await providers.BringAsync(g, f.Me.Id, default);
        f.Me.PrivateChatStartedAt = f.Clock.Now; await f.Db.SaveChangesAsync();
        var transport = new RejectTransport();
        await new NotificationDispatcher(f.Db, f.Clock, transport, providers).ProcessOneAsync(default);
        Assert.Equal(NotificationState.Expired, Assert.Single(f.Db.Notifications).State);
    }

    [Fact]
    public async Task Discovery_DeduplicatesAndProtectsPrivateOwnership_WithExplainableCounts()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(2));
        var club = f.Db.Clubs.Local.Single(); club.CollectionJson = ClubCollectionSerializer.Serialize(new(2,
            [new(42, "Игра", null, null, 1, 8, null, [])]));
        var original = club.CollectionJson;
        AddOwned(f, f.Me, 42); AddOwned(f, f.Me, 43); AddOwned(f, f.Other, 99); await f.Db.SaveChangesAsync();
        var (catalog, providers, _) = Services(f);
        var all = await catalog.ListAsync("club", BotMode.Club, f.Me.TelegramUserId, new(null, null, [], [], "name"), default);
        Assert.Equal(2, all.Items.Count); Assert.DoesNotContain(all.Items, x => x.BggId == 99);
        var same = Assert.Single(all.Items, x => x.BggId == 42);
        Assert.Equal(1, same.ScheduledGatherings); Assert.Contains("Есть в клубе · Есть у вас", same.AvailabilitySummary);
        var unplanned = await catalog.ListAsync("club", BotMode.Club, f.Me.TelegramUserId, new(null, null, [], [], "popular", "mine", null, "unplanned"), default);
        Assert.Equal(43, Assert.Single(unplanned.Items).BggId);
        Assert.Equal(GameProviderState.ClubProvided, (await providers.ForGatheringAsync(g, f.Me.Id, default)).State);
        Assert.Equal(original, club.CollectionJson);
    }

    [Fact]
    public async Task Dashboards_ReuseSchedule_AndLimitOrganizerToAuthorizedScope()
    {
        await using var f = new PlanningFixture(); var mine = f.Gathering("club", f.Clock.Now.AddHours(1));
        var other = f.Gathering("club", f.Clock.Now.AddHours(2)); other.OrganizerParticipant = f.Other; other.OrganizerParticipantId = f.Other.Id;
        var elsewhere = f.Gathering("private", f.Clock.Now.AddHours(1));
        await f.Db.SaveChangesAsync(); var (_, providers, _) = Services(f);
        var dashboards = new GatheringDashboardService(f.Db, providers, f.Clock);
        var personal = await dashboards.PersonalAsync(f.Me.Id, ["club"], default);
        Assert.Equal(mine.PublicId, Assert.Single(personal.Items).PublicId);
        Assert.DoesNotContain(personal.Items, x => x.PublicId == elsewhere.PublicId);
        var organizer = await dashboards.OrganizerAsync(f.Me.Id, "club", false, default);
        Assert.Equal(mine.PublicId, Assert.Single(organizer.Items).PublicId);
        Assert.True(organizer.Items[0].BelowMinimum); Assert.True(organizer.Items[0].StartingSoon);
        Assert.Equal(2, (await dashboards.OrganizerAsync(f.Me.Id, "club", true, default)).Items.Count);
    }

    private static (GameCatalogService, GameProviderService, CampContributionSelectionService) Services(PlanningFixture f)
    {
        var policy = new CampParticipationPolicy(f.Db, f.Clock); var contributions = new CampContributionSelectionService(f.Db, policy, f.Clock);
        var effective = new EffectiveCampCatalogService(f.Db, contributions); var catalog = new GameCatalogService(f.Db, effective, f.Clock);
        return (catalog, new(f.Db, catalog, effective, policy, contributions, f.Clock), contributions);
    }
    private static Camp AddCamp(PlanningFixture f, string key)
    {
        var camp = new Camp { BotChat = new() { Key = key, Name = key, Mode = BotMode.Camp, TimeZoneId = "Asia/Almaty", IsActive = true },
            Status = CampStatus.Active, StartsAtUtc = f.Clock.Now.AddHours(-2), EndsAtUtc = f.Clock.Now.AddDays(3),
            BaseCollectionJson = ClubCollectionSerializer.Serialize(new(2, [])) };
        f.Db.Camps.Add(camp); return camp;
    }
    private static CampRegistration Register(PlanningFixture f, Camp camp, Participant p, DateTimeOffset date)
    {
        var row = new CampRegistration { Camp = camp, Participant = p, City = "Алматы", NeedsAccommodation = false,
            SelectedDays = [new() { Date = CommunityTime.LocalDate(date, camp.BotChat.TimeZoneId) }] };
        f.Db.CampRegistrations.Add(row); return row;
    }
    private static void AddOwned(PlanningFixture f, Participant p, long bggId) => f.Db.ParticipantCollectionItems.Add(new()
    {
        Participant = p, BggId = bggId, ItemType = CollectionItemType.BaseGame, Source = CollectionItemSource.Manual,
        SnapshotJson = CollectionItemSnapshotSerializer.Serialize(new(CollectionItemSnapshot.CurrentVersion, "Игра", null, null, 1, 8, null))
    });
    private sealed class RejectTransport : INotificationTransport
    {
        public Task<NotificationReceipt> SendAsync(Notification notification, Participant recipient, CancellationToken ct) =>
            throw new InvalidOperationException("Resolved provider notice must not be sent");
    }
}
