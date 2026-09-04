using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class WishlistRecruitmentTests
{
    [Fact]
    public async Task WishesAreIdempotentScopedAndIndependentOfOwnershipAndNames()
    {
        await using var f = new PlanningFixture();
        f.Gathering("club", f.Clock.Now.AddHours(1)); f.Gathering("other", f.Clock.Now.AddHours(1));
        await f.Db.SaveChangesAsync();
        var bgg = new WishBgg(); var (catalog, wishes) = Services(f, bgg);
        await wishes.SetAsync("club", f.Me.Id, 42, true, default);
        bgg.Name = "Локализованное название";
        await wishes.SetAsync("club", f.Me.Id, 42, true, default);
        Assert.Single(f.Db.GameWishes); Assert.Equal(1, bgg.Calls);
        Assert.Empty(f.Db.ParticipantCollectionItems); Assert.Empty(f.Db.CampGameContributions);
        var game = Assert.Single(await catalog.LoadAsync("club", BotMode.Club, f.Me.TelegramUserId, default));
        Assert.True(game.IsWished); Assert.False(game.IsOwned); Assert.False(game.IsInBaseCollection); Assert.Empty(game.Game.Expansions);
        Assert.Empty(await catalog.LoadAsync("club", BotMode.Club, f.Other.TelegramUserId, default));
        Assert.Empty(await catalog.LoadAsync("other", BotMode.Club, f.Me.TelegramUserId, default));
        await wishes.SetAsync("other", f.Me.Id, 42, true, default);
        await wishes.SetAsync("club", f.Me.Id, 42, false, default);
        await wishes.SetAsync("club", f.Me.Id, 42, false, default);
        Assert.Equal("other", Assert.Single(f.Db.GameWishes).CommunityKey);
    }

    [Fact]
    public async Task ExpansionsCannotBecomeWishesFromCatalogOrBgg()
    {
        await using var f = new PlanningFixture(); f.Gathering("club", f.Clock.Now.AddHours(1));
        f.Db.ParticipantCollectionItems.Add(new() { Participant = f.Me, BggId = 99, ItemType = CollectionItemType.Expansion,
            SnapshotJson = CollectionItemSnapshotSerializer.Serialize(new(1, "Дополнение", null, null, 1, 4, null, ParentBggIds: [42])) });
        await f.Db.SaveChangesAsync();
        var (catalog, wishes) = Services(f, new WishBgg());
        Assert.False((await catalog.DetailsAsync("club", BotMode.Club, f.Me.TelegramUserId, 99, default)).CanWish);
        await Assert.ThrowsAsync<ArgumentException>(() => wishes.SetAsync("club", f.Me.Id, 99, true, default));
        await Assert.ThrowsAsync<ArgumentException>(() => wishes.SetAsync("club", f.Me.Id, 100, true, default));
        Assert.Empty(f.Db.GameWishes);
    }

    [Fact]
    public async Task CreationNotifiesMatchingWishOnceButNotOrganizerAndEditsDoNotResend()
    {
        await using var f = new PlanningFixture(); var seed = f.Gathering("club", f.Clock.Now.AddHours(8));
        await f.Db.SaveChangesAsync(); var (_, wishes) = Services(f, new WishBgg());
        await wishes.SetAsync("club", f.Me.Id, 42, true, default);
        await wishes.SetAsync("club", f.Other.Id, 42, true, default);
        f.Other.PrivateChatStartedAt = f.Clock.Now; await f.Db.SaveChangesAsync();
        var notices = new GatheringNotificationService(f.Db, new(f.Db, f.Clock));
        var management = new GatheringManagementService(f.Db, new(f.Db, new WishBgg()), new(f.Db, f.Clock), notices, f.Clock);
        var g = await management.CreateAsync(seed.Community.ToBotCommunity(), new(f.Me.TelegramUserId, null, "Первый"),
            new("club", "bgg", 42, [], f.Clock.Now.AddHours(1), 1, 3, 4, null, true), default);
        var row = Assert.Single(f.Db.Notifications);
        Assert.Equal(NotificationKind.WishlistGathering, row.Kind); Assert.Equal(f.Other.Id, row.ParticipantId);
        await notices.NotifyWishlistAsync(g, default);
        await management.UpdateAsync(g.PublicId, "club", f.Me.TelegramUserId,
            new(f.Clock.Now.AddHours(2), 1, 3, 4, "Новое описание", true, []), default);
        var transport = new ReceiptTransport();
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(1, transport.Calls); Assert.Contains("1/4", row.Text);
        Assert.Single(f.Db.Notifications.Where(x => x.Kind == NotificationKind.WishlistGathering));
        await notices.NotifyWishlistAsync(g, default);
        Assert.False(await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default));
        var unrelated = f.Gathering("club", f.Clock.Now.AddHours(4), 43);
        await f.Db.SaveChangesAsync(); await notices.NotifyWishlistAsync(unrelated, default);
        Assert.Single(f.Db.Notifications);
    }

    [Theory]
    [InlineData("disabled", NotificationState.SuppressedByPreference)]
    [InlineData("joined", NotificationState.Expired)]
    [InlineData("removed", NotificationState.Expired)]
    [InlineData("cancelled", NotificationState.Expired)]
    [InlineData("private", NotificationState.CannotMessageUser)]
    public async Task WishlistDeliveryRechecksCurrentEligibility(string change, NotificationState expected)
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        await f.Db.SaveChangesAsync(); var (_, wishes) = Services(f, new WishBgg());
        await wishes.SetAsync("club", f.Other.Id, 42, true, default);
        await new GatheringNotificationService(f.Db, new(f.Db, f.Clock)).NotifyWishlistAsync(g, default);
        if (change != "private") f.Other.PrivateChatStartedAt = f.Clock.Now;
        if (change == "disabled") f.Db.NotificationPreferences.Add(new() { Participant = f.Other, WishlistGathering = false });
        if (change == "joined") g.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Waitlisted });
        if (change == "removed") await wishes.SetAsync("club", f.Other.Id, 42, false, default);
        if (change == "cancelled") g.Status = GatheringStatus.Cancelled;
        await f.Db.SaveChangesAsync(); var transport = new ReceiptTransport();
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(expected, Assert.Single(f.Db.Notifications).State); Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData(1, 0, "Нужны +2")]
    [InlineData(2, 0, "Нужен +1")]
    [InlineData(3, 1, "+2 до оптимального")]
    [InlineData(4, 1, "+1 до оптимального")]
    [InlineData(5, 2, "Можно ещё +2")]
    [InlineData(6, 2, "Можно ещё +1")]
    [InlineData(7, 3, "Состав набран")]
    public void RecruitmentUsesCanonicalSeatsIncludingGuestsExcludingWaitlist(int occupied, int priority, string text)
    {
        var g = new GameGathering { MinimumPlayers = 3, DesiredPlayers = 5, MaximumPlayers = 7,
            Guests = Enumerable.Range(1, occupied - 1).Select(i => new GameGatheringGuest()).ToList(),
            Participants = [new() { Status = GatheringParticipationStatus.Waitlisted }] };
        var state = GatheringRecruitment.Describe(g);
        Assert.Equal(priority, state.Priority); Assert.Contains(text, state.Text); Assert.Equal(7 - occupied, state.FreeSeats);
    }

    [Fact]
    public async Task DigestRanksAllGatheringsWithin36HoursAndCapsTheMessage()
    {
        await using var f = new PlanningFixture();
        var yellow = f.Gathering("club", f.Clock.Now.AddHours(1)); yellow.MinimumPlayers = 1;
        var green = f.Gathering("club", f.Clock.Now.AddHours(2)); green.MinimumPlayers = green.DesiredPlayers = 1;
        var red = f.Gathering("club", f.Clock.Now.AddHours(3));
        for (var i = 0; i < 7; i++) f.Gathering("club", f.Clock.Now.AddHours(4 + i));
        var expired = f.Gathering("club", f.Clock.Now); var far = f.Gathering("club", f.Clock.Now.AddHours(36).AddTicks(1));
        var full = f.Gathering("club", f.Clock.Now.AddHours(1)); full.MaximumPlayers = 1; full.Status = GatheringStatus.Full;
        var cancelled = f.Gathering("club", f.Clock.Now.AddHours(1)); cancelled.Status = GatheringStatus.Cancelled;
        var completed = f.Gathering("club", f.Clock.Now.AddHours(1)); completed.Status = GatheringStatus.Completed;
        var closed = f.Gathering("club", f.Clock.Now.AddHours(1)); closed.Status = GatheringStatus.Closed;
        await f.Db.SaveChangesAsync();
        var ranked = GatheringRecruitment.Rank(f.Db.GameGatherings.Local, f.Clock.Now);
        Assert.Equal(red.PublicId, ranked[0].PublicId); Assert.Equal(yellow.PublicId, ranked[^2].PublicId); Assert.Equal(green.PublicId, ranked[^1].PublicId);
        foreach (var excluded in new[] { expired, far, full, cancelled, completed, closed }) Assert.DoesNotContain(excluded, ranked);
        var message = RecruitmentDigestFormatter.Build(f.Db.GameGatherings.Local, "club", "Asia/Almaty", f.Clock.Now, "TestBot");
        Assert.Equal(7, message.Shown); Assert.Equal(10, message.Total); Assert.Contains("Ещё 3 сбора", message.Text);
        Assert.Equal(8, message.Keyboard.InlineKeyboard.Count()); Assert.True(message.Text.Length < 4000);
        var boundary = f.Gathering("club", f.Clock.Now.AddHours(36)); Assert.True(GatheringRecruitment.IsRelevant(boundary, f.Clock.Now));
        Assert.False(GatheringRecruitment.CanRequest(green, f.Me.Id, f.Clock.Now));
    }

    [Fact]
    public async Task CooldownIsSharedAcrossOrganizersAndChangesImmediately()
    {
        await using var f = new PlanningFixture(); var a = f.Gathering("club", f.Clock.Now.AddHours(8));
        var b = f.Gathering("club", f.Clock.Now.AddHours(9)); b.OrganizerParticipant = f.Other; b.OrganizerParticipantId = f.Other.Id;
        var elsewhere = f.Gathering("other", f.Clock.Now.AddHours(8)); await f.Db.SaveChangesAsync();
        var service = new RecruitmentDigestService(f.Db, f.Clock);
        Assert.Equal(4, a.Community.RecruitmentCooldownHours);
        Assert.True((await service.RequestAsync("club", a.PublicId, f.Me.Id, default)).Queued);
        f.Db.RecruitmentDigests.Local.Single().State = RecruitmentDigestState.Delivered; await f.Db.SaveChangesAsync();
        f.Clock.Now = f.Clock.Now.AddMinutes(30);
        var result = await new RecruitmentDigestService(f.Db, f.Clock).RequestAsync("club", b.PublicId, f.Other.Id, default);
        Assert.False(result.Queued); Assert.Contains("3 ч 30 мин", result.Message);
        Assert.True((await service.RequestAsync("other", elsewhere.PublicId, f.Me.Id, default)).Queued);
        await service.SetCooldownAsync("club", 2, default); f.Clock.Now = f.Clock.Now.AddMinutes(91);
        Assert.True((await service.RequestAsync("club", b.PublicId, f.Other.Id, default)).Queued);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetCooldownAsync("club", 0, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetCooldownAsync("club", 25, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsync("club", a.PublicId, f.Other.Id, default));
    }

    internal static (GameCatalogService, GameWishService) Services(PlanningFixture f, IBoardGameGeekClient bgg)
    {
        var contributions = new CampContributionSelectionService(f.Db, new(f.Db, f.Clock), f.Clock);
        var catalog = new GameCatalogService(f.Db, new(f.Db, contributions), f.Clock);
        return (catalog, new(f.Db, catalog, bgg, f.Clock));
    }
    private sealed class ReceiptTransport : INotificationTransport
    {
        public int Calls;
        public Task<NotificationReceipt> SendAsync(Notification n, Participant p, CancellationToken ct)
        { Calls++; return Task.FromResult(new NotificationReceipt(123)); }
    }
    internal sealed class WishBgg : IBoardGameGeekClient
    {
        public string Name = "Игра"; public int Calls;
        public Task<BggGameDetails?> GetGameDetailsAsync(long id, CancellationToken ct)
        { Calls++; return Task.FromResult<BggGameDetails?>(id == 100 ? null : new(new ExternalGame(id, Name, 1, 8, null, null), [])); }
        public Task<IReadOnlyList<BggBaseGameSearchResult>> SearchAsync(string q, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => throw new NotSupportedException();
    }
}
