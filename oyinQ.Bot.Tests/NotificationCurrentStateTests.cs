using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;

namespace oyinQ.Bot.Tests;

public sealed class NotificationCurrentStateTests
{
    [Theory]
    [InlineData(NotificationKind.GatheringTimeChanged)]
    [InlineData(NotificationKind.GatheringDetailsChanged)]
    [InlineData(NotificationKind.GatheringFull)]
    public async Task CancelledGatheringExpiresStateNoticesButDeliversCancellation(NotificationKind kind)
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        f.Me.PrivateChatStartedAt = f.Clock.Now;
        f.Db.NotificationPreferences.Add(new() { Participant = f.Me, GatheringFull = true });
        await f.Db.SaveChangesAsync();
        var queue = new NotificationService(f.Db, f.Clock);
        await queue.EnqueueAsync(new(f.Me.TelegramUserId, kind, "change", "old", "club", g.PublicId), default);
        await queue.EnqueueAsync(new(f.Me.TelegramUserId, NotificationKind.GatheringCancelled, "cancel", "Отмена", "club", g.PublicId), default);
        g.Status = GatheringStatus.Cancelled;
        await f.Db.SaveChangesAsync();
        var transport = new RecordingTransport();
        var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        while (await dispatcher.ProcessOneAsync(default)) { }
        Assert.Equal(NotificationState.Expired, f.Db.Notifications.Single(x => x.Kind == kind).State);
        Assert.Equal("Отмена", Assert.Single(transport.Texts));
    }

    [Fact]
    public async Task MultipleQueuedTimeChangesDeliverOnlyCurrentTime()
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        f.Other.PrivateChatStartedAt = f.Clock.Now;
        g.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Waitlisted });
        await f.Db.SaveChangesAsync();
        var notifications = new GatheringNotificationService(f.Db, new(f.Db, f.Clock));
        await notifications.NotifyTimeChangedAsync(g.PublicId, default);
        g.StartsAtUtc = g.StartsAtUtc.AddHours(3); g.UpdatedAt = g.UpdatedAt.AddMinutes(1);
        await f.Db.SaveChangesAsync();
        await notifications.NotifyTimeChangedAsync(g.PublicId, default);
        var transport = new RecordingTransport(); var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        while (await dispatcher.ProcessOneAsync(default)) { }
        Assert.Contains(GatheringPresentationService.FormatLocalDateTime(g.StartsAtUtc, g.Community.TimeZoneId), Assert.Single(transport.Texts));
        Assert.Single(f.Db.Notifications.Where(x => x.State == NotificationState.Expired));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullNoticeExpiresAfterASeatOpensOrRecipientLeaves(bool leave)
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(1));
        g.MaximumPlayers = g.DesiredPlayers = 2;
        var signup = new GameGatheringParticipant { Participant = f.Other, Status = GatheringParticipationStatus.Confirmed };
        g.Participants.Add(signup); f.Other.PrivateChatStartedAt = f.Clock.Now;
        f.Db.NotificationPreferences.Add(new() { Participant = f.Other, GatheringFull = true });
        await f.Db.SaveChangesAsync();
        await new NotificationService(f.Db, f.Clock).EnqueueAsync(new(f.Other.TelegramUserId, NotificationKind.GatheringFull, "full", "old", "club", g.PublicId), default);
        if (leave) signup.Status = GatheringParticipationStatus.Withdrawn;
        else g.MaximumPlayers = 4;
        await f.Db.SaveChangesAsync();
        var transport = new RecordingTransport();
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Empty(transport.Texts);
        Assert.Equal(NotificationState.Expired, Assert.Single(f.Db.Notifications).State);
    }

    [Fact]
    public async Task PlannerFailuresDoNotBlockAlreadyQueuedDelivery()
    {
        await using var f = new PlanningFixture(); f.Me.PrivateChatStartedAt = f.Clock.Now;
        await f.Db.SaveChangesAsync();
        await new NotificationService(f.Db, f.Clock).EnqueueAsync(new(f.Me.TelegramUserId, NotificationKind.GatheringCancelled, "cancel", "Отмена"), default);
        var transport = new RecordingTransport();
        var services = new ServiceCollection();
        services.AddSingleton(f.Db); services.AddSingleton<TimeProvider>(f.Clock);
        services.AddSingleton<INotificationTransport>(transport); services.AddScoped<NotificationDispatcher>();
        // Deliberately failing factories exercise all planner failures before delivery in the same iteration.
        services.AddScoped<GatheringReminderService>(_ => throw new InvalidOperationException("planner unavailable"));
        services.AddScoped<ProviderAttentionService>(_ => throw new InvalidOperationException("planner unavailable"));
        services.AddScoped<PlayConfirmationReminderService>(_ => throw new InvalidOperationException("planner unavailable"));
        await using var provider = services.BuildServiceProvider();
        var worker = new NotificationWorker(provider.GetRequiredService<IServiceScopeFactory>(), f.Clock, NullLogger<NotificationWorker>.Instance);
        await worker.RunIterationAsync(default);
        Assert.Equal("Отмена", Assert.Single(transport.Texts));
    }

    private sealed class RecordingTransport : INotificationTransport
    {
        public List<string> Texts { get; } = [];
        public Task<NotificationReceipt> SendAsync(Notification notification, Participant recipient, CancellationToken ct)
        { Texts.Add(notification.Text); return Task.FromResult(new NotificationReceipt(1)); }
    }
}
