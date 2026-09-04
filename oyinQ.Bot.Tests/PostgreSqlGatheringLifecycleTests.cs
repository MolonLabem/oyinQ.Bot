using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task UnderfilledLifecycleRetainsHistoryAndRosterAndNotifiesOnceDespitePublicationFailure()
    {
        await using var database = await Database.CreateAsync();
        var actor = await SeedAsync(database);
        Guid id;
        await using (var db = database.Open())
        {
            var organizer = await db.Participants.SingleAsync(); organizer.PrivateChatStartedAt = Now;
            var confirmed = new Participant { TelegramUserId = 23456, DisplayName = "Участник", PrivateChatStartedAt = Now };
            var waiting = new Participant { TelegramUserId = 34567, DisplayName = "Ожидающий", PrivateChatStartedAt = Now };
            var g = new GameGathering
            {
                CommunityKey = "club", OrganizerParticipantId = actor.Id, StartsAtUtc = Now,
                MinimumPlayers = 4, DesiredPlayers = 4, MaximumPlayers = 5,
                Status = GatheringStatus.Recruiting, GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(Snapshot()),
                TelegramChatId = -10012345, TelegramMessageId = 99,
                Participants = [new() { Participant = confirmed, Status = GatheringParticipationStatus.Confirmed, JoinedAt = Now.AddHours(-1) },
                    new() { Participant = waiting, Status = GatheringParticipationStatus.Waitlisted, JoinedAt = Now }],
                Guests = [new() { DisplayName = "Гость", CreatedByParticipantId = actor.Id, CreatedAt = Now, UpdatedAt = Now }],
                Expansions = [new() { BggId = 43, Name = "Дополнение" }]
            };
            db.GameGatherings.Add(g); await db.SaveChangesAsync(); id = g.PublicId;
        }

        // This fake rejects publication edits: cancellation and its outbox must already be committed.
        using var http = new HttpClient(new ReleaseBot());
        var bot = new Telegram.Bot.TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", http);
        var services = new ServiceCollection();
        services.AddScoped(_ => database.Open());
        services.AddScoped(sp => new GatheringNotificationService(sp.GetRequiredService<AppDbContext>(), new(sp.GetRequiredService<AppDbContext>(), Time)));
        services.AddScoped(sp => new GatheringPublicationService(sp.GetRequiredService<AppDbContext>(),
            new CommunityStore(sp.GetRequiredService<AppDbContext>()),
            new(new ReleaseSender(), bot, new(), NullLogger<GatheringTelegramPublisher>.Instance), Time,
            NullLogger<GatheringPublicationService>.Instance));
        await using var provider = services.BuildServiceProvider();
        using var worker = new GatheringLifecycleWorker(provider.GetRequiredService<IServiceScopeFactory>(), Time, NullLogger<GatheringLifecycleWorker>.Instance);
        var outcomes = await Task.WhenAll(worker.ProcessOneAsync(default), worker.ProcessOneAsync(default));
        Assert.Single(outcomes, x => x);
        Assert.False(await worker.ProcessOneAsync(default));

        await using var verify = database.Open();
        var saved = await verify.GameGatherings.Include(x => x.Participants).Include(x => x.Guests).Include(x => x.Expansions)
            .Include(x => x.OrganizerParticipant).SingleAsync();
        Assert.Equal(id, saved.PublicId); Assert.Equal(GatheringStatus.Cancelled, saved.Status);
        Assert.Equal(GatheringLifecycle.InsufficientParticipantsReason, saved.CancellationReason);
        Assert.Equal(Now, saved.CancelledAt); Assert.Null(saved.CompletedAt);
        Assert.Equal(2, saved.Participants.Count); Assert.Single(saved.Guests); Assert.Single(saved.Expansions);
        Assert.All(saved.Participants, p => Assert.Equal(AttendanceOutcome.Unknown, p.AttendanceOutcome));
        Assert.Equal(99, saved.TelegramMessageId); Assert.Equal(GatheringPublicationStatus.Failed, saved.PublicationStatus);
        Assert.Empty(await verify.TelegramMessageCleanups.ToArrayAsync());
        Assert.Single(await GatheringListQuery.Apply(verify.GameGatherings, GatheringListScope.Cancelled, Now).ToArrayAsync());
        var presentation = new GatheringPresentationService();
        var community = (await verify.OyinQCommunities.SingleAsync()).ToBotCommunity();
        Assert.Equal(saved.CancellationReason, presentation.BuildCard(saved, community).CancellationReason);
        Assert.Equal(saved.CancellationReason, presentation.BuildDetails(saved, community).CancellationReason);
        Assert.Contains(GatheringLifecycle.InsufficientParticipantsReason, presentation.BuildTelegramAnnouncement(saved, community).HtmlText);
        var notifications = await verify.Notifications.Include(x => x.Participant).ToArrayAsync();
        Assert.Equal(new long[] { 12345, 23456 }, notifications.Select(x => x.Participant.TelegramUserId).Order().ToArray());
        Assert.All(notifications, n => { Assert.Equal(NotificationKind.GatheringFailed, n.Kind); Assert.Equal(id, n.GatheringPublicId); Assert.Equal("club", n.CommunityKey); });
        var transport = new Transport();
        var dispatcher = new NotificationDispatcher(verify, Time, transport);
        Assert.True(await dispatcher.ProcessOneAsync(default)); Assert.True(await dispatcher.ProcessOneAsync(default));
        Assert.False(await dispatcher.ProcessOneAsync(default)); Assert.Equal(2, transport.Calls);
    }
}
