using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;

namespace oyinQ.Bot.Tests;

public sealed class NotificationDeliveryTests
{
    [Fact]
    public async Task FullOptIn_IsDeliveredOnceEvenAfterLeavingAndRefilling()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        g.MaximumPlayers = g.DesiredPlayers = 2;
        f.Me.PrivateChatStartedAt = f.Other.PrivateChatStartedAt = f.Clock.Now;
        f.Db.NotificationPreferences.AddRange(new() { Participant = f.Me, GatheringFull = true }, new() { Participant = f.Other, GatheringFull = true });
        await f.Db.SaveChangesAsync();
        var notifications = new GatheringNotificationService(f.Db, new NotificationService(f.Db, f.Clock));
        var service = new GatheringService(f.Db, new Features.Communities.CampParticipationPolicy(f.Db, f.Clock), notificationService: notifications);
        await service.JoinAsync(g.PublicId, "club", f.Other.TelegramUserId, f.Clock.Now, default);
        var transport = new Transport(new NotificationReceipt(12)); var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        while (await dispatcher.ProcessOneAsync(default)) { }
        Assert.Equal(2, f.Db.Notifications.Count(x => x.Kind == NotificationKind.GatheringFull && x.State == NotificationState.Delivered));
        await service.LeaveAsync(g.PublicId, "club", f.Other.TelegramUserId, f.Clock.Now.AddSeconds(1), default);
        await service.JoinAsync(g.PublicId, "club", f.Other.TelegramUserId, f.Clock.Now.AddSeconds(2), default);
        while (await dispatcher.ProcessOneAsync(default)) { }
        Assert.Equal(2, f.Db.Notifications.Count(x => x.Kind == NotificationKind.GatheringFull));
    }

    [Fact]
    public async Task PrivateStart_IsSeparateFromIdentity_AndDeduplicatedNoticeResumesAfterStart()
    {
        await using var f = new PlanningFixture();
        var gathering = f.Gathering("club", f.Clock.Now.AddHours(1));
        gathering.Participants.Add(new() { Participant = f.Me, Status = GatheringParticipationStatus.Confirmed, JoinedAt = f.Clock.Now });
        await f.Db.SaveChangesAsync();
        var intent = new NotificationIntent(f.Me.TelegramUserId, NotificationKind.WaitlistPromotion,
            $"{gathering.PublicId:N}:{f.Clock.Now.UtcTicks}", "Освободилось место", "club", gathering.PublicId);
        var service = new NotificationService(f.Db, f.Clock);
        await service.EnqueueAsync(intent, default); await service.EnqueueAsync(intent, default);
        var transport = new Transport(new NotificationReceipt(123)); var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        await dispatcher.ProcessOneAsync(default);
        var row = Assert.Single(f.Db.Notifications);
        Assert.Equal(NotificationState.CannotMessageUser, row.State); Assert.Equal(0, transport.Calls);
        Assert.False(await dispatcher.ProcessOneAsync(default));
        f.Clock.Now = f.Clock.Now.AddMinutes(1); f.Me.PrivateChatStartedAt = f.Clock.Now; await f.Db.SaveChangesAsync();
        await dispatcher.ProcessOneAsync(default);
        Assert.Equal(NotificationState.Delivered, row.State); Assert.Equal(123, row.TelegramMessageId);
        Assert.False(await dispatcher.ProcessOneAsync(default)); Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Preferences_AreRecheckedAtDelivery_EssentialCannotBeDisabled()
    {
        await using var f = new PlanningFixture(); f.Me.PrivateChatStartedAt = f.Clock.Now;
        var prefs = new NotificationPreferences { Participant = f.Me, GatheringFull = true };
        f.Db.NotificationPreferences.Add(prefs); await f.Db.SaveChangesAsync();
        var service = new NotificationService(f.Db, f.Clock);
        await service.EnqueueAsync(new(f.Me.TelegramUserId, NotificationKind.GatheringFull, "full", "Все собрались"), default);
        await service.EnqueueAsync(new(f.Me.TelegramUserId, NotificationKind.GatheringCancelled, "cancel", "Отмена"), default);
        prefs.GatheringFull = false; await f.Db.SaveChangesAsync();
        var transport = new Transport(new NotificationReceipt(1)); var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        while (await dispatcher.ProcessOneAsync(default)) { }
        Assert.Equal(NotificationState.SuppressedByPreference, f.Db.Notifications.Single(x => x.Kind == NotificationKind.GatheringFull).State);
        Assert.Equal(NotificationState.Delivered, f.Db.Notifications.Single(x => x.Kind == NotificationKind.GatheringCancelled).State);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(0, new NotificationPreferences().ReminderLeadMinutes);
    }

    [Fact]
    public async Task ExplicitTransientFailure_RetriesOnceDue_AndSuccessNeverResends()
    {
        await using var f = new PlanningFixture(); f.Me.PrivateChatStartedAt = f.Clock.Now; await f.Db.SaveChangesAsync();
        await new NotificationService(f.Db, f.Clock).EnqueueAsync(new(f.Me.TelegramUserId, NotificationKind.GatheringCancelled, "c", "Отмена"), default);
        var transport = new Transport(new NotificationReceipt(null, "telegram_429", Retryable: true), new(9));
        var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        await dispatcher.ProcessOneAsync(default); var row = Assert.Single(f.Db.Notifications);
        Assert.Equal(NotificationState.Failed, row.State); Assert.Equal(1, row.AttemptCount);
        Assert.False(await dispatcher.ProcessOneAsync(default));
        f.Clock.Now = row.NextAttemptAt; await dispatcher.ProcessOneAsync(default);
        Assert.Equal(NotificationState.Delivered, row.State); Assert.Equal(2, row.AttemptCount);
        Assert.False(await dispatcher.ProcessOneAsync(default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownOrCrashedDelivery_IsNotAutomaticallyResent(bool crashed)
    {
        await using var f = new PlanningFixture(); f.Me.PrivateChatStartedAt = f.Clock.Now; await f.Db.SaveChangesAsync();
        await new NotificationService(f.Db, f.Clock).EnqueueAsync(new(f.Me.TelegramUserId, NotificationKind.GatheringCancelled, "c", "Отмена"), default);
        var row = Assert.Single(f.Db.Notifications);
        if (crashed) { row.State = NotificationState.Delivering; row.LeaseExpiresAt = f.Clock.Now.AddMinutes(-1); await f.Db.SaveChangesAsync(); }
        var transport = new Transport(new NotificationReceipt(null, "response_lost", Uncertain: true));
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(NotificationState.DeliveryUnknown, row.State);
        f.Clock.Now = f.Clock.Now.AddDays(1);
        Assert.False(await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default));
        Assert.Equal(crashed ? 0 : 1, transport.Calls);
    }

    [Fact]
    public async Task BlockedBot_IsPersisted_AndCannotBeRetriedWithoutPrivateUpdate()
    {
        await using var f = new PlanningFixture(); f.Me.PrivateChatStartedAt = f.Clock.Now; await f.Db.SaveChangesAsync();
        await new NotificationService(f.Db, f.Clock).EnqueueAsync(new(f.Me.TelegramUserId, NotificationKind.GatheringCancelled, "c", "Отмена"), default);
        var transport = new Transport(new NotificationReceipt(null, "telegram_403", CannotMessage: true));
        var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport); await dispatcher.ProcessOneAsync(default);
        Assert.NotNull(f.Me.TelegramDeliveryBlockedAt);
        f.Clock.Now = f.Clock.Now.AddDays(1); Assert.False(await dispatcher.ProcessOneAsync(default));
        Assert.Equal(NotificationState.CannotMessageUser, Assert.Single(f.Db.Notifications).State);
    }

    [Fact]
    public async Task Reminder_RechecksTimeMembershipAndPreferences_ExcludesWaitlist()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddMinutes(40));
        g.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Waitlisted });
        f.Me.PrivateChatStartedAt = f.Other.PrivateChatStartedAt = f.Clock.Now;
        f.Db.NotificationPreferences.AddRange(new() { Participant = f.Me, ReminderLeadMinutes = 60 }, new() { Participant = f.Other, ReminderLeadMinutes = 60 });
        await f.Db.SaveChangesAsync();
        var queue = new NotificationService(f.Db, f.Clock); var reminders = new GatheringReminderService(f.Db, queue, f.Clock);
        await reminders.EnqueueDueAsync(default); await reminders.EnqueueDueAsync(default);
        Assert.Equal(f.Me.Id, Assert.Single(f.Db.Notifications).ParticipantId);
        g.StartsAtUtc = f.Clock.Now.AddHours(3); await f.Db.SaveChangesAsync();
        var transport = new Transport(new NotificationReceipt(10)); var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        await dispatcher.ProcessOneAsync(default); Assert.Equal(0, transport.Calls);
        f.Clock.Now = g.StartsAtUtc.AddMinutes(-60); await dispatcher.ProcessOneAsync(default);
        Assert.Equal(NotificationState.Delivered, Assert.Single(f.Db.Notifications).State);
        g.StartsAtUtc = f.Clock.Now.AddMinutes(30); await f.Db.SaveChangesAsync(); await reminders.EnqueueDueAsync(default);
        Assert.False(await dispatcher.ProcessOneAsync(default)); Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("leave")]
    [InlineData("disable")]
    public async Task PendingReminder_IsSuppressedAfterCurrentStateChanges(string change)
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddMinutes(40));
        g.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Confirmed });
        f.Other.PrivateChatStartedAt = f.Clock.Now;
        var preference = new NotificationPreferences { Participant = f.Other, ReminderLeadMinutes = 60 };
        f.Db.NotificationPreferences.Add(preference); await f.Db.SaveChangesAsync();
        await new GatheringReminderService(f.Db, new NotificationService(f.Db, f.Clock), f.Clock).EnqueueDueAsync(default);
        if (change == "cancel") g.Status = GatheringStatus.Cancelled;
        if (change == "leave") g.Participants.Single().Status = GatheringParticipationStatus.Withdrawn;
        if (change == "disable") preference.ReminderLeadMinutes = 0;
        await f.Db.SaveChangesAsync(); var transport = new Transport(new NotificationReceipt(10));
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(0, transport.Calls);
        Assert.Equal(change == "disable" ? NotificationState.SuppressedByPreference : NotificationState.Expired, Assert.Single(f.Db.Notifications).State);
    }

    private sealed class Transport(params NotificationReceipt[] results) : INotificationTransport
    {
        public int Calls { get; private set; }
        public Task<NotificationReceipt> SendAsync(Notification notification, Participant recipient, CancellationToken ct) =>
            Task.FromResult(results[Math.Min(Calls++, results.Length - 1)]);
    }

    [Theory]
    [InlineData(NotificationState.Expired, true, 1)]
    [InlineData(NotificationState.SuppressedByPreference, true, 1)]
    [InlineData(NotificationState.Delivered, true, 0)]
    [InlineData(NotificationState.DeliveryUnknown, true, 0)]
    [InlineData(NotificationState.Expired, false, 0)]
    public async Task ReminderReconsiderationPreservesTerminalStatesAndCurrentEligibility(NotificationState prior, bool eligible, int calls)
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddMinutes(40));
        g.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Confirmed });
        f.Other.PrivateChatStartedAt = f.Clock.Now;
        f.Db.NotificationPreferences.Add(new() { Participant = f.Other, ReminderLeadMinutes = 60 }); await f.Db.SaveChangesAsync();
        var reminders = new GatheringReminderService(f.Db, new(f.Db, f.Clock), f.Clock);
        await reminders.EnqueueDueAsync(default); (await f.Db.Notifications.SingleAsync()).State = prior;
        if (!eligible) g.Status = GatheringStatus.Cancelled;
        await f.Db.SaveChangesAsync();
        await reminders.EnqueueDueAsync(default);
        var transport = new Transport(new NotificationReceipt(10));
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(calls, transport.Calls); Assert.Single(f.Db.Notifications);
    }

    [Theory]
    [InlineData("current", 1)]
    [InlineData("cancelled", 0)]
    [InlineData("withdrawn", 0)]
    [InlineData("rejoined", 0)]
    public async Task PromotionRequiresTheSameCurrentSignup(string scenario, int calls)
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddMinutes(40));
        var signup = new GameGatheringParticipant { Participant = f.Other, Status = GatheringParticipationStatus.Confirmed, JoinedAt = f.Clock.Now };
        g.Participants.Add(signup); f.Other.PrivateChatStartedAt = f.Clock.Now; await f.Db.SaveChangesAsync();
        await new GatheringNotificationService(f.Db, new(f.Db, f.Clock)).NotifyPromotionsAsync("club", g.PublicId, [GatheringPromotion.Capture(f.Other)], default);
        if (scenario == "cancelled") g.Status = GatheringStatus.Cancelled;
        if (scenario == "withdrawn") signup.Status = GatheringParticipationStatus.Withdrawn;
        if (scenario == "rejoined") signup.JoinedAt = f.Clock.Now.AddMinutes(1);
        await f.Db.SaveChangesAsync(); var transport = new Transport(new NotificationReceipt(10));
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(calls, transport.Calls);
    }
}
