using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task CampUnregistrationRollsBackCancellationRegistrationAndContributionsWhenNotificationPlanningFails()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database, camp: true);
        long campId; Guid gatheringId;
        await using (var initial = database.Open())
        {
            campId = await initial.Camps.Select(x => x.Id).SingleAsync();
            var community = (await initial.OyinQCommunities.SingleAsync()).ToBotCommunity();
            var gathering = await Management(initial, new Bgg()).CreateAsync(community, new(actor.TelegramUserId),
                Command() with { AddToCollection = true, BringToCamp = true }, default);
            gatheringId = gathering.PublicId;
            gathering.Participants.Add(new() { Participant = new() { TelegramUserId = 12346, DisplayName = "Участник" },
                Status = GatheringParticipationStatus.Confirmed, JoinedAt = Now });
            await initial.SaveChangesAsync();
        }
        await using (var failing = database.Open(new RejectCancellationNotice()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new CampRegistrationService(failing, Time)
                .UnregisterAsync(campId, actor.Id, default));
        await using (var verify = database.Open())
        {
            Assert.Single(await verify.CampRegistrations.ToArrayAsync());
            Assert.Single(await verify.CampGameContributions.ToArrayAsync());
            Assert.Single(await verify.ParticipantCollectionItems.ToArrayAsync());
            Assert.NotEqual(GatheringStatus.Cancelled, (await verify.GameGatherings.SingleAsync()).Status);
            Assert.Empty(await verify.Notifications.Where(x => x.Kind == NotificationKind.GatheringCancelled).ToArrayAsync());
        }
        await using (var success = database.Open())
        {
            var result = await new CampRegistrationService(success, Time).UnregisterAsync(campId, actor.Id, default);
            Assert.Equal(gatheringId, Assert.Single(result.CancelledGatheringIds));
            Assert.Equal(gatheringId, Assert.Single(result.ChangedGatheringIds));
        }
        await using var final = database.Open();
        Assert.Empty(await final.CampRegistrations.ToArrayAsync()); Assert.Empty(await final.CampGameContributions.ToArrayAsync());
        Assert.Single(await final.ParticipantCollectionItems.ToArrayAsync());
        Assert.Equal(GatheringStatus.Cancelled, (await final.GameGatherings.SingleAsync()).Status);
        Assert.Single(await final.Notifications.Where(x => x.Kind == NotificationKind.GatheringCancelled).ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task CampUnregistrationWaitsForCreationAndCancelsTheCommittedGathering()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database, camp: true);
        var paused = Signal(); var resume = Signal(); var unregisterEntered = Signal();
        await using var createDb = database.Open(new SaveGate(paused, resume));
        await using var unregisterDb = database.Open(new CampUnregisterLockObserver(unregisterEntered));
        var campId = await unregisterDb.Camps.Select(x => x.Id).SingleAsync();
        var community = (await createDb.OyinQCommunities.SingleAsync()).ToBotCommunity();
        var creation = Management(createDb, new Bgg()).CreateAsync(community, new(actor.TelegramUserId), Command(), default);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<CampRegistrationMutationResult>? unregister = null;
        try
        {
            unregister = new CampRegistrationService(unregisterDb, Time).UnregisterAsync(campId, actor.Id, default);
            await unregisterEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(unregister.IsCompleted);
        }
        finally { resume.TrySetResult(); }
        var gathering = await creation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(gathering.PublicId, Assert.Single((await unregister!.WaitAsync(TimeSpan.FromSeconds(10))).CancelledGatheringIds));
        await using var verify = database.Open();
        Assert.Empty(await verify.CampRegistrations.ToArrayAsync());
        Assert.Equal(GatheringStatus.Cancelled, (await verify.GameGatherings.SingleAsync()).Status);
    }

    [PostgreSqlFact]
    public async Task CampUnregistrationDuringProviderLookupPreventsLaterCreation()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database, camp: true);
        var entered = Signal(); var resume = Signal();
        await using var createDb = database.Open();
        var community = (await createDb.OyinQCommunities.SingleAsync()).ToBotCommunity();
        var creation = Management(createDb, new Bgg(async () => { entered.TrySetResult(); await resume.Task; }))
            .CreateAsync(community, new(actor.TelegramUserId), Command(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var db = database.Open();
            await new CampRegistrationService(db, Time).UnregisterAsync(await db.Camps.Select(x => x.Id).SingleAsync(), actor.Id, default);
        }
        finally { resume.TrySetResult(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => creation);
        await using var verify = database.Open();
        Assert.Empty(await verify.GameGatherings.ToArrayAsync()); Assert.Empty(await verify.CampRegistrations.ToArrayAsync());
    }

    private sealed class RejectCancellationNotice : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<Notification>().Any(x => x.State == EntityState.Added && x.Entity.Kind == NotificationKind.GatheringCancelled))
                throw new InvalidOperationException("Проверка общего отката отмены и уведомлений.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CampUnregisterLockObserver(TaskCompletionSource entered) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("\"Camps\"") && command.CommandText.Contains("FOR UPDATE")) entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
