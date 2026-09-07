using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;

namespace oyinQ.Bot.Tests;

public sealed class PlayConfirmationReminderTests
{
    private static PlayConfirmationReminderService Planner(PlanningFixture f) => new(f.Db, new(f.Db, f.Clock), f.Clock);
    private static void Estimate(GameGathering g, int? minimum, int? maximum) => g.GameSnapshotJson =
        GatheringGameSnapshotSerializer.Serialize(GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson)
            with { MinPlayTimeMinutes = minimum, MaxPlayTimeMinutes = maximum });

    [Theory]
    [InlineData(30, 90, 120)]
    [InlineData(45, null, 75)]
    [InlineData(null, null, 150)]
    [InlineData(-1, 0, 150)]
    [InlineData(null, int.MaxValue, 150)]
    public async Task PlannerWaitsForEstimatePlusGrace_AndOnlyNotifiesOrganizerOnce(int? min, int? max, int expected)
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("club", f.Clock.Now.AddMinutes(-expected).AddSeconds(1));
        g.Status = GatheringStatus.Completed; Estimate(g, min, max);
        g.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Confirmed });
        f.Me.PrivateChatStartedAt = f.Clock.Now;
        await f.Db.SaveChangesAsync();
        await Planner(f).EnqueueDueAsync(default);
        Assert.Empty(f.Db.Notifications);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        await Planner(f).EnqueueDueAsync(default); await Planner(f).EnqueueDueAsync(default);
        var row = Assert.Single(f.Db.Notifications);
        Assert.Equal(f.Me.Id, row.ParticipantId);
        Assert.Equal(g.PublicId, row.GatheringPublicId);
        Assert.True(NotificationPolicy.IsEssential(row.Kind));
        var transport = new Transport();
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(1, transport.Calls); Assert.Contains("уже закончилась?", row.Text);
        await Planner(f).EnqueueDueAsync(default);
        Assert.False(await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default));
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("played")]
    [InlineData("not-played")]
    [InlineData("cancelled")]
    [InlineData("deleted")]
    [InlineData("organizer-changed")]
    [InlineData("expired")]
    [InlineData("wrong-community")]
    public async Task DispatcherRechecksOutcomeCommunityOrganizerAndExpiry(string change)
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(-3));
        g.Status = GatheringStatus.Completed; f.Me.PrivateChatStartedAt = f.Clock.Now;
        await f.Db.SaveChangesAsync(); await Planner(f).EnqueueDueAsync(default);
        var row = await f.Db.Notifications.SingleAsync();
        switch (change)
        {
            case "played": g.ConfirmedWasPlayed = true; break;
            case "not-played": g.ConfirmedWasPlayed = false; break;
            case "cancelled": g.Status = GatheringStatus.Cancelled; break;
            case "deleted": g.Community.DeletedAt = f.Clock.Now; break;
            case "organizer-changed": g.OrganizerParticipant = f.Other; g.OrganizerParticipantId = f.Other.Id; break;
            case "expired": f.Clock.Now = f.Clock.Now.AddDays(1); break;
            case "wrong-community": row.CommunityKey = "different"; break;
        }
        await f.Db.SaveChangesAsync();
        var transport = new Transport();
        await new NotificationDispatcher(f.Db, f.Clock, transport).ProcessOneAsync(default);
        Assert.Equal(0, transport.Calls); Assert.Equal(NotificationState.Expired, row.State);
    }

    [Fact]
    public async Task PlannerSkipsCancelledAcknowledgedAndOldHistory()
    {
        await using var f = new PlanningFixture();
        var cancelled = f.Gathering("club", f.Clock.Now.AddHours(-3)); cancelled.Status = GatheringStatus.Cancelled;
        var acknowledged = f.Gathering("club", f.Clock.Now.AddHours(-3)); acknowledged.Status = GatheringStatus.Completed; acknowledged.ConfirmedWasPlayed = false;
        var old = f.Gathering("club", f.Clock.Now.AddDays(-3)); old.Status = GatheringStatus.Completed;
        await f.Db.SaveChangesAsync(); await Planner(f).EnqueueDueAsync(default);
        Assert.Empty(f.Db.Notifications);
    }

    [Fact]
    public async Task PostponedEstimateDefersDelivery_AndUnknownDeliveryIsNotRevived()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(-3));
        g.Status = GatheringStatus.Completed; f.Me.PrivateChatStartedAt = f.Clock.Now;
        await f.Db.SaveChangesAsync(); await Planner(f).EnqueueDueAsync(default);
        g.StartsAtUtc = f.Clock.Now.AddHours(-1); await f.Db.SaveChangesAsync();
        var transport = new Transport { Unknown = true };
        var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        await dispatcher.ProcessOneAsync(default);
        var row = await f.Db.Notifications.SingleAsync();
        Assert.Equal(NotificationState.Pending, row.State);
        Assert.Equal(GatheringPlayTiming.ConfirmationDueAt(g), row.NextAttemptAt);
        Assert.Equal(0, transport.Calls);
        f.Clock.Now = row.NextAttemptAt;
        await dispatcher.ProcessOneAsync(default);
        Assert.Equal(NotificationState.DeliveryUnknown, row.State);
        await Planner(f).EnqueueDueAsync(default);
        Assert.False(await dispatcher.ProcessOneAsync(default)); Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task PrivateStartRecoveryDoesNotSendAfterOutcomeWasRecorded()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(-3));
        g.Status = GatheringStatus.Completed; await f.Db.SaveChangesAsync(); await Planner(f).EnqueueDueAsync(default);
        var transport = new Transport(); var dispatcher = new NotificationDispatcher(f.Db, f.Clock, transport);
        await dispatcher.ProcessOneAsync(default);
        Assert.Equal(NotificationState.CannotMessageUser, (await f.Db.Notifications.SingleAsync()).State);
        g.ConfirmedWasPlayed = true; f.Clock.Now = f.Clock.Now.AddMinutes(1); f.Me.PrivateChatStartedAt = f.Clock.Now;
        await f.Db.SaveChangesAsync(); await dispatcher.ProcessOneAsync(default);
        Assert.Equal(0, transport.Calls); Assert.Equal(NotificationState.Expired, (await f.Db.Notifications.SingleAsync()).State);
    }

    private sealed class Transport : INotificationTransport
    {
        public int Calls; public bool Unknown;
        public Task<NotificationReceipt> SendAsync(Notification notification, Participant recipient, CancellationToken ct)
        { Calls++; return Task.FromResult(Unknown ? new NotificationReceipt(null, Uncertain: true) : new NotificationReceipt(1)); }
    }
}
