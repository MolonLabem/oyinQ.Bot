using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Integrations.Telegram;
using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    private static ProfileDeletionService ProfileDeletion(AppDbContext db) => new(db, Time, new(db, new(db, Time)));

    [PostgreSqlFact]
    public async Task ProfileDeletionErasesPersonalDataAcrossCommunitiesAndPreservesAnonymousSharedHistory()
    {
        await using var database = await Database.CreateAsync();
        var actor = await SeedAsync(database, camp: true);
        long otherId; long futureId; long ownFutureId; long pastId;
        await using (var db = database.Open())
        {
            var participant = await db.Participants.SingleAsync();
            participant.TelegramUsername = "private_username"; participant.PreferredDisplayName = "Личное имя";
            participant.PrivateChatStartedAt = Now; participant.ActiveCommunityKey = "club";
            var other = new Participant { TelegramUserId = 8888, DisplayName = "Другой игрок", PrivateChatStartedAt = Now };
            var waiting = new Participant { TelegramUserId = 9999, DisplayName = "Очередь", PrivateChatStartedAt = Now };
            db.Participants.AddRange(other, waiting);
            db.OyinQCommunities.Add(new() { Key = "other", Name = "Другой клуб", IsActive = false });
            await db.SaveChangesAsync(); otherId = other.Id;
            var future = GatheringRules.Create("other", Snapshot(), other.Id, Now.AddHours(1), 1, 2, 2, null, false, Now);
            future.Participants.Add(new() { ParticipantId = actor.Id, Status = GatheringParticipationStatus.Confirmed, JoinedAt = Now, DisplayNameOverride = "Имя в сборе" });
            future.Participants.Add(new() { ParticipantId = waiting.Id, Status = GatheringParticipationStatus.Waitlisted, JoinedAt = Now.AddMinutes(1) });
            var own = GatheringRules.Create("club", Snapshot(), actor.Id, Now.AddHours(2), 1, 2, 4, null, false, Now);
            own.OrganizerDisplayNameOverride = "Личное имя организатора";
            own.Participants.Add(new() { ParticipantId = other.Id, Status = GatheringParticipationStatus.Confirmed, JoinedAt = Now });
            var past = GatheringRules.Create("club", Snapshot(), actor.Id, Now.AddHours(2), 1, 2, 4, null, false, Now);
            past.StartsAtUtc = Now.AddDays(-1); past.Status = GatheringStatus.Completed;
            past.OrganizerDisplayNameOverride = "Историческое имя";
            db.GameGatherings.AddRange(future, own, past); await db.SaveChangesAsync();
            futureId = future.Id; ownFutureId = own.Id; pastId = past.Id;
            var camp = await db.Camps.SingleAsync();
            camp.CreatedByTelegramUserId = actor.TelegramUserId;
            var registration = await db.CampRegistrations.SingleAsync();
            registration.DisplayName = "Имя на кэмпе";
            registration.RegistrationDataJson = "{\"version\":1,\"answers\":{\"phone\":\"private answer\"}}";
            await new ParticipantCollectionService(db).UpsertAsync(actor.Id, [Item()], CollectionItemSource.Manual, Now, default);
            await new ParticipantCollectionService(db).UpsertAsync(other.Id, [Item()], CollectionItemSource.Manual, Now, default);
            db.CampGameContributions.Add(new() { CampId = camp.Id, ParticipantId = actor.Id, BggId = 42, SnapshotJson = "{}" });
            db.GameWishes.Add(new() { CommunityKey = "other", ParticipantId = actor.Id, BggId = 42, SnapshotJson = "{}" });
            db.CampParticipantVisibilities.Add(new() { CampId = camp.Id, ParticipantId = actor.Id });
            var request = new CampBringRequest { CampId = camp.Id, OwnerParticipantId = actor.Id, BggId = 42, SnapshotJson = "{}" };
            request.Requesters.Add(new() { ParticipantId = other.Id }); db.CampBringRequests.Add(request);
            db.CampBggImports.Add(new() { PublicId = Guid.NewGuid(), ParticipantId = actor.Id, BggUsername = "private_bgg", Status = CampBggImportStatus.Running,
                DraftJson = "{}", ConfirmationJson = "{}", Error = "private_bgg", LeaseId = Guid.NewGuid(), LeaseExpiresAt = Now.AddMinutes(30) });
            db.NotificationPreferences.Add(new() { ParticipantId = actor.Id });
            db.Notifications.AddRange(new Notification { ParticipantId = actor.Id, Text = "Личная информация", DeduplicationKey = "own", Kind = NotificationKind.ImportCompleted, NextAttemptAt = Now },
                new Notification { ParticipantId = other.Id, ActorParticipantId = actor.Id, Text = "Личное имя", DeduplicationKey = "other-delivered", State = NotificationState.Delivered });
            db.ChatAdminPermissions.Add(new() { CommunityKey = "club", TelegramUserId = actor.TelegramUserId, DisplayName = "Личное имя" });
            db.ChatAdminPermissions.Add(new() { CommunityKey = "other", TelegramUserId = other.TelegramUserId,
                DisplayName = "Другой игрок", GrantedByTelegramUserId = actor.TelegramUserId });
            db.PendingTelegramPeerSelections.Add(new() { PublicId = Guid.NewGuid(), RequestId = 7, RequestedByTelegramUserId = actor.TelegramUserId, ResultJson = "{}" });
            db.GatheringPlayRecords.Add(new() { GatheringId = past.Id, WasPlayed = true, EndedAtUtc = Now.AddHours(-1), RecordedByParticipantId = actor.Id,
                GameSnapshotJson = past.GameSnapshotJson, Players = [new() { SourcePlayerId = actor.PublicId, ParticipantId = actor.Id, DisplayName = "Личное имя", Score = 42, IsWinner = true }] });
            db.Entry(past).Property<string?>("LegacyPlayOutcomeJson").CurrentValue = $"{{\"RecordedByParticipantId\":{actor.Id},\"ExternalUrl\":\"https://private.example\",\"players\":[{{\"ParticipantId\":{actor.Id},\"DisplayName\":\"Личное имя\"}}]}}";
            await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
        {
            await ProfileDeletion(db).DeleteAsync(actor.TelegramUserId, default);
            await ProfileDeletion(db).DeleteAsync(actor.TelegramUserId, default);
        }
        await using (var db = database.Open())
        {
            var erased = await db.Participants.SingleAsync(x => x.Id == actor.Id);
            Assert.NotNull(erased.DeletedAt); Assert.Equal(-actor.Id, erased.TelegramUserId);
            Assert.Equal(ProfileDeletionService.AnonymousName, erased.DisplayName);
            Assert.Null(erased.TelegramUsername); Assert.Null(erased.PreferredDisplayName); Assert.Null(erased.ActiveCommunityKey);
            Assert.Null(erased.PrivateChatStartedAt); Assert.Null(ParticipantPresentation.GetContactUrl(erased));
            Assert.DoesNotContain("tg://", ParticipantPresentation.ToHtmlLink(erased));
            Assert.Single(await db.DeletedProfiles.ToArrayAsync());
            Assert.Equal(otherId, (await db.ParticipantCollectionItems.SingleAsync()).ParticipantId);
            Assert.Empty(await db.CampRegistrations.ToArrayAsync()); Assert.Empty(await db.CampRegistrationDays.ToArrayAsync());
            Assert.Empty(await db.CampGameContributions.ToArrayAsync()); Assert.Empty(await db.GameWishes.ToArrayAsync());
            Assert.Empty(await db.CampParticipantVisibilities.ToArrayAsync()); Assert.Empty(await db.CampBringRequests.ToArrayAsync());
            Assert.Empty(await db.CampBringRequesters.ToArrayAsync()); Assert.Empty(await db.NotificationPreferences.ToArrayAsync());
            Assert.Empty(await db.Notifications.Where(x => x.ParticipantId == actor.Id).ToArrayAsync());
            var permission = await db.ChatAdminPermissions.SingleAsync();
            Assert.Equal(8888, permission.TelegramUserId); Assert.Equal(0, permission.GrantedByTelegramUserId);
            Assert.Equal(0, (await db.Camps.SingleAsync()).CreatedByTelegramUserId);
            Assert.Empty(await db.PendingTelegramPeerSelections.ToArrayAsync());
            var import = await db.CampBggImports.SingleAsync();
            Assert.Equal("", import.BggUsername); Assert.Null(import.DraftJson); Assert.Null(import.ConfirmationJson); Assert.Null(import.Error);
            Assert.Null(import.LeaseId); Assert.Equal(CampBggImportStatus.Cancelled, import.Status);
            var future = await db.GameGatherings.Include(x => x.Participants).SingleAsync(x => x.Id == futureId);
            Assert.Equal(GatheringParticipationStatus.Withdrawn, future.Participants.Single(x => x.ParticipantId == actor.Id).Status);
            Assert.Equal(GatheringParticipationStatus.Confirmed, future.Participants.Single(x => x.ParticipantId != actor.Id).Status);
            Assert.Equal(GatheringStatus.Cancelled, (await db.GameGatherings.SingleAsync(x => x.Id == ownFutureId)).Status);
            var past = await db.GameGatherings.SingleAsync(x => x.Id == pastId);
            Assert.Equal(GatheringStatus.Completed, past.Status); Assert.Null(past.OrganizerDisplayNameOverride);
            var legacy = db.Entry(past).Property<string?>("LegacyPlayOutcomeJson").CurrentValue!;
            Assert.DoesNotContain("Личное", legacy); Assert.DoesNotContain("private.example", legacy);
            var played = await db.GatheringPlayPlayers.SingleAsync();
            Assert.Equal(ProfileDeletionService.AnonymousName, played.DisplayName); Assert.Equal(42, played.Score); Assert.True(played.IsWinner);
            Assert.Contains(await db.Notifications.ToArrayAsync(), x => x.Kind == NotificationKind.WaitlistPromotion);
            Assert.Contains(await db.Notifications.ToArrayAsync(), x => x.Kind == NotificationKind.GatheringCancelled);
            Assert.Equal(NotificationState.Delivered, (await db.Notifications.SingleAsync(x => x.DeduplicationKey == "other-delivered")).State);
            await Assert.ThrowsAsync<ProfileDeletedException>(() => new ParticipantIdentityService(db, Time).GetOrCreateAsync(actor.TelegramUserId, "again", "Имя", null, default, true));
            await Assert.ThrowsAsync<ProfileDeletedException>(() => new ParticipantCollectionService(db).UpsertAsync(actor.Id, [Item()], CollectionItemSource.Manual, Now, default));
        }
        await using (var db = database.Open())
            await ProfileDeletion(db).RecreateAsync(new(actor.TelegramUserId, "new", "Новое имя"), default);
        await using (var db = database.Open())
        {
            var fresh = await db.Participants.SingleAsync(x => x.TelegramUserId == actor.TelegramUserId);
            Assert.NotEqual(actor.Id, fresh.Id); Assert.NotEqual(actor.PublicId, fresh.PublicId);
            Assert.Null(fresh.PrivateChatStartedAt); Assert.Null(fresh.DeletedAt);
            Assert.Empty(await db.ParticipantCollectionItems.Where(x => x.ParticipantId == fresh.Id).ToArrayAsync());
            Assert.Empty(await db.DeletedProfiles.ToArrayAsync());
            Assert.NotNull((await db.Participants.SingleAsync(x => x.Id == actor.Id)).DeletedAt);
        }
    }

    [PostgreSqlFact]
    public async Task ProfileDeletionMigrationPreservesPopulatedProfilesAndCanBeAppliedTwice()
    {
        await using var database = await Database.CreateAsync("20261003063649_CampCustomization");
        await using (var db = database.Open())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Participants\" (\"TelegramUserId\", \"DisplayName\", \"CreatedAt\", \"UpdatedAt\") VALUES (12345, 'Имя', {Now}, {Now})");
            await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        }
        await using var verify = database.Open();
        Assert.Null((await verify.Participants.SingleAsync()).DeletedAt);
        Assert.Empty(await verify.DeletedProfiles.ToArrayAsync());
        await ProfileDeletion(verify).DeleteAsync(12345, default);
        Assert.Single(await verify.DeletedProfiles.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task ProfileDeletionWaitsForAnExistingRequestAndBlocksLaterIdentityRefreshes()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        await using var requestDb = database.Open();
        await using var deleteDb = database.Open();
        var operation = await ParticipantOperationLock.AcquireAsync(requestDb, actor.TelegramUserId, default);
        Task? deletion = null;
        try
        {
            deletion = ProfileDeletion(deleteDb).DeleteAsync(actor.TelegramUserId, default);
            await Task.Delay(100); Assert.False(deletion.IsCompleted);
            await new ParticipantCollectionService(requestDb).UpsertAsync(actor.Id, [Item()], CollectionItemSource.Manual, Now, default);
        }
        finally { await operation.DisposeAsync(); }
        await deletion!.WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = database.Open();
        Assert.Empty(await verify.ParticipantCollectionItems.ToArrayAsync());
        await Assert.ThrowsAsync<ProfileDeletedException>(() => new ParticipantIdentityService(verify, Time).GetOrCreateAsync(actor.TelegramUserId, null, "Имя", null, default));
    }

    [PostgreSqlFact]
    public async Task ProfileDeletionInvalidatesAnImportBetweenItsLastReadAndCompletionWrite()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        await using (var db = database.Open()) await Imports(db).QueueAsync(null, actor.Id, "private_bgg", default);
        var entered = Signal(); var resume = Signal();
        var services = new ServiceCollection();
        services.AddScoped(_ => database.Open(new ImportCompletionGate(entered, resume)));
        services.AddScoped(_ => new CampBggImportService(new Bgg(importGames: [new ExternalGame(42, "Игра", 1, 4, null, "https://boardgamegeek.com/boardgame/42")])));
        services.AddScoped(p => new NotificationService(p.GetRequiredService<AppDbContext>(), Time));
        await using var provider = services.BuildServiceProvider();
        var worker = new CampBggImportWorker(provider.GetRequiredService<IServiceScopeFactory>(), Time, NullLogger<CampBggImportWorker>.Instance);
        var processing = worker.ProcessOneAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var db = database.Open();
            await ProfileDeletion(db).DeleteAsync(actor.TelegramUserId, default);
        }
        finally { resume.TrySetResult(); }
        Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(10)));
        await using var verify = database.Open();
        var import = await verify.CampBggImports.SingleAsync();
        Assert.Equal(CampBggImportStatus.Cancelled, import.Status);
        Assert.Equal("", import.BggUsername); Assert.Null(import.DraftJson); Assert.Null(import.LeaseId);
        Assert.Empty(await verify.Notifications.ToArrayAsync());
        Assert.Empty(await verify.ParticipantCollectionItems.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task ProfileDeletionWaitsForAnInFlightDeliveryAndStopsAllLaterMessages()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        await using (var db = database.Open())
        {
            (await db.Participants.SingleAsync()).PrivateChatStartedAt = Now; await db.SaveChangesAsync();
            await new NotificationService(db, Time).EnqueueAsync(new(actor.TelegramUserId, NotificationKind.GatheringCancelled, "already-sending", "Отмена"), default);
        }
        var entered = Signal(); var resume = Signal();
        await using var sendDb = database.Open(); await using var deleteDb = database.Open();
        var dispatcher = new NotificationDispatcher(sendDb, Time, new DeliveryGate(entered, resume));
        var sending = dispatcher.ProcessOneAsync(default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deletion = ProfileDeletion(deleteDb).DeleteAsync(actor.TelegramUserId, default);
        try { await Task.Delay(100); Assert.False(deletion.IsCompleted); }
        finally { resume.TrySetResult(); }
        Assert.True(await sending); await deletion.WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = database.Open();
        await new NotificationService(verify, Time).EnqueueAsync(new(actor.TelegramUserId, NotificationKind.GatheringCancelled, "after-erasure", "Отмена"), default);
        Assert.Empty(await verify.Notifications.ToArrayAsync());
        var transport = new Transport(); Assert.False(await new NotificationDispatcher(verify, Time, transport).ProcessOneAsync(default));
        Assert.Equal(0, transport.Calls);
    }

    private sealed class ImportCompletionGate(TaskCompletionSource entered, TaskCompletionSource resume) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("UPDATE \"CampBggImports\"") && command.CommandText.Contains("\"DraftJson\""))
            { entered.TrySetResult(); await resume.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); }
            return result;
        }
    }

    private sealed class DeliveryGate(TaskCompletionSource entered, TaskCompletionSource resume) : INotificationTransport
    {
        public async Task<NotificationReceipt> SendAsync(Notification notification, Participant recipient, CancellationToken ct)
        { entered.TrySetResult(); await resume.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); return new(123); }
    }
}
