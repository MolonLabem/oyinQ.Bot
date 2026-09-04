using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

// Permanent regression coverage for the September stabilization audit.
public sealed class AuditRegressionTests
{
    [Fact]
    public void NormalizedReferenceFitsDatabaseColumn()
    {
        var input = "https://app.bgstatsapp.com/" + new string('я', 400);
        Assert.True(input.Length < 2048);
        Assert.Throws<ArgumentException>(() => ExternalPlayReferenceService.Normalize(input));
    }

    [Fact]
    public async Task ReferenceAuthorCanRemoveAfterRosterCorrection()
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("club", f.Clock.Now.AddHours(-2));
        g.Status = GatheringStatus.Completed;
        g.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Confirmed });
        await f.Db.SaveChangesAsync();
        var plays = new GatheringPlayService(f.Db, f.Clock);
        await plays.SaveAsync(g.PublicId, "club", f.Me.Id, new(true, f.Clock.Now, null, [f.Me.PublicId, f.Other.PublicId], [], 0), default);
        var references = new ExternalPlayReferenceService(f.Db, f.Clock);
        await references.AddAsync(g.PublicId, "club", f.Other.Id, "https://app.bgstatsapp.com/play/1", default);
        var reference = await f.Db.GatheringExternalPlayReferences.SingleAsync();
        await plays.SaveAsync(g.PublicId, "club", f.Me.Id, new(true, f.Clock.Now, null, [f.Me.PublicId], [], 1), default);
        await references.RemoveAsync(g.PublicId, "club", f.Other.Id, reference.Id, default);
        Assert.Empty(f.Db.GatheringExternalPlayReferences);
    }

    [Fact]
    public async Task ReminderCanResumeAfterTemporarilyDisablingPreference()
    {
        await using var f = new PlanningFixture();
        f.Gathering("club", f.Clock.Now.AddMinutes(40));
        f.Me.PrivateChatStartedAt = f.Clock.Now;
        var pref = new NotificationPreferences { Participant = f.Me, ReminderLeadMinutes = 60 };
        f.Db.NotificationPreferences.Add(pref); await f.Db.SaveChangesAsync();
        var reminders = new GatheringReminderService(f.Db, new NotificationService(f.Db, f.Clock), f.Clock);
        var transport = new Transport(); var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        await reminders.EnqueueDueAsync(default);
        pref.ReminderLeadMinutes = 0; await f.Db.SaveChangesAsync();
        await dispatcher.ProcessOneAsync(default);
        pref.ReminderLeadMinutes = 60; await f.Db.SaveChangesAsync();
        await reminders.EnqueueDueAsync(default); await dispatcher.ProcessOneAsync(default);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task LatePrivateStartDoesNotDeliverPromotionForCancelledGathering()
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("club", f.Clock.Now.AddHours(1)); await f.Db.SaveChangesAsync();
        await new NotificationService(f.Db, f.Clock).EnqueueAsync(new(f.Me.TelegramUserId, NotificationKind.WaitlistPromotion,
            "promotion", "Для вас освободилось место", "club", g.PublicId), default);
        var transport = new Transport(); var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        await dispatcher.ProcessOneAsync(default);
        g.Status = GatheringStatus.Cancelled; f.Clock.Now = f.Clock.Now.AddMinutes(1);
        f.Me.PrivateChatStartedAt = f.Clock.Now; await f.Db.SaveChangesAsync();
        await dispatcher.ProcessOneAsync(default);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task ConfirmedImportReplaysAfterDraftExpiry()
    {
        await using var f = new PlanningFixture();
        var confirmation = CampBggImportCoordinator.BuildConfirmation(new(CampBggImportDraft.CurrentVersion, "owner", []), [], []);
        var import = new CampBggImport { Participant = f.Me, PublicId = Guid.NewGuid(), BggUsername = "owner",
            Status = CampBggImportStatus.Confirmed, ExpiresAt = f.Clock.Now.AddDays(-1),
            DraftJson = CampBggImportDraftSerializer.Serialize(new(CampBggImportDraft.CurrentVersion, "owner", [])),
            ConfirmationJson = CampBggImportConfirmationSerializer.Serialize(confirmation) };
        f.Db.CampBggImports.Add(import); await f.Db.SaveChangesAsync();
        var policy = new CampParticipationPolicy(f.Db, f.Clock);
        var coordinator = new CampBggImportCoordinator(f.Db, new(f.Db, policy, f.Clock), policy, f.Clock);
        Assert.True((await coordinator.ConfirmAsync(import.PublicId, null, f.Me.Id, [], [], default)).WasAlreadyConfirmed);
    }

    private sealed class Transport : INotificationTransport
    {
        public int Calls;
        public Task<NotificationReceipt> SendAsync(Notification n, Participant p, CancellationToken ct)
        { Calls++; return Task.FromResult(new NotificationReceipt(123)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationRechecksStateAfterProviderLookup(bool deleteCommunity)
    {
        await using var f = new PlanningFixture();
        var seed = f.Gathering("club", f.Clock.Now.AddDays(1)); await f.Db.SaveChangesAsync();
        var community = seed.Community.ToBotCommunity();
        var bgg = new DelayedBgg(async () => {
            if (deleteCommunity) { seed.Community.IsActive = false; seed.Community.DeletedAt = f.Clock.Now; await f.Db.SaveChangesAsync(); }
            else f.Clock.Now = f.Clock.Now.AddMinutes(2);
        });
        var service = new GatheringManagementService(f.Db, new(f.Db, bgg), new(f.Db, f.Clock),
            new(f.Db, new NotificationService(f.Db, f.Clock)), f.Clock);
        var command = new CreateGatheringCommand("club", "bgg", 42, [], f.Clock.Now.AddMinutes(1), 1, 3, 4, null, false, true);
        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(community,
            new(f.Me.TelegramUserId, null, "Первый"), command, default));
        Assert.Single(f.Db.GameGatherings);
    }

    private sealed class DelayedBgg(Func<Task> duringLookup) : IBoardGameGeekClient
    {
        public async Task<BggGameDetails?> GetGameDetailsAsync(long id, CancellationToken ct)
        { await duringLookup(); return new(new ExternalGame(42, "Игра", 1, 4, null, "https://boardgamegeek.com/boardgame/42"), []); }
        public Task<IReadOnlyList<BggBaseGameSearchResult>> SearchAsync(string q, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => throw new NotSupportedException();
    }
}
