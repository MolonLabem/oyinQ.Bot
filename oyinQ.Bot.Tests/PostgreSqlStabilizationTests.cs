using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OYINQ_TEST_POSTGRES")))
            Skip = "Задайте OYINQ_TEST_POSTGRES для отдельного тестового PostgreSQL с правом CREATE DATABASE.";
    }
}

public sealed partial class PostgreSqlStabilizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private static readonly Clock Time = new();
    private static CampBggImportDraftItem Item(long id = 42) => new(id, CollectionItemType.BaseGame, null,
        new(CollectionItemSnapshot.CurrentVersion, "Игра", null, null, 1, 4, null));

    [PostgreSqlFact]
    public async Task DeletionCommittedDuringBggLookupPreventsCreation()
    {
        await using var database = await Database.CreateAsync();
        var actor = await SeedAsync(database);
        var entered = Signal(); var resume = Signal();
        await using var createDb = database.Open();
        var community = (await createDb.OyinQCommunities.SingleAsync()).ToBotCommunity();
        var creation = Management(createDb, new Bgg(async () => { entered.TrySetResult(); await resume.Task; }))
            .CreateAsync(community, new(actor.TelegramUserId, null, "Игрок"), Command(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var deleteDb = database.Open();
            await Deletion(deleteDb, actor.TelegramUserId).DeleteClubAsync(actor.TelegramUserId,
                await deleteDb.Clubs.Select(x => x.Id).SingleAsync(), default);
        }
        finally { resume.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => creation);
        await using var verify = database.Open();
        Assert.Empty(await verify.GameGatherings.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task DeletionWaitsForCreationLockAndCancelsTheCommittedGathering()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        var createPaused = Signal(); var releaseCreate = Signal(); var deleteLockEntered = Signal();
        await using var createDb = database.Open(new SaveGate(createPaused, releaseCreate));
        await using var deleteDb = database.Open(new LockObserver(deleteLockEntered));
        var community = (await createDb.OyinQCommunities.SingleAsync()).ToBotCommunity();
        var create = Management(createDb, new Bgg()).CreateAsync(community,
            new(actor.TelegramUserId, null, "Игрок"), Command(), default);
        await createPaused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<CommunityDeletionResult>? deletion = null;
        try
        {
            deletion = Deletion(deleteDb, actor.TelegramUserId).DeleteClubAsync(actor.TelegramUserId,
                await deleteDb.Clubs.Select(x => x.Id).SingleAsync(), default);
            await deleteLockEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(deletion.IsCompleted);
        }
        finally { releaseCreate.TrySetResult(); }
        await create.WaitAsync(TimeSpan.FromSeconds(10));
        await deletion!.WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = database.Open();
        Assert.NotNull((await verify.OyinQCommunities.SingleAsync()).DeletedAt);
        Assert.Equal(GatheringStatus.Cancelled, (await verify.GameGatherings.SingleAsync()).Status);
    }

    [PostgreSqlFact]
    public async Task ExternalCampCreationRollsBackOwnershipAndCommitmentOnLocalFailure()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database, camp: true);
        await using (var failing = database.Open(new RejectGathering()))
        {
            var community = (await failing.OyinQCommunities.SingleAsync()).ToBotCommunity();
            await Assert.ThrowsAsync<InvalidOperationException>(() => Management(failing, new Bgg()).CreateAsync(community,
                new(actor.TelegramUserId, null, "Игрок"), Command() with { AddToCollection = true, BringToCamp = true }, default));
        }
        await using (var verify = database.Open())
        {
            Assert.Empty(await verify.ParticipantCollectionItems.ToArrayAsync());
            Assert.Empty(await verify.CampGameContributions.ToArrayAsync());
            Assert.Empty(await verify.GameGatherings.ToArrayAsync());
        }
        await using (var success = database.Open())
        {
            var community = (await success.OyinQCommunities.SingleAsync()).ToBotCommunity();
            await Management(success, new Bgg()).CreateAsync(community, new(actor.TelegramUserId, null, "Игрок"),
                Command() with { AddToCollection = true, BringToCamp = true }, default);
        }
        await using var saved = database.Open();
        Assert.Single(await saved.ParticipantCollectionItems.ToArrayAsync());
        Assert.Equal(CampBringCommitment.Bringing, (await saved.CampGameContributions.SingleAsync()).Commitment);
        Assert.Single(await saved.GameGatherings.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task ConcurrentIdentityAndCollectionUpsertDeduplicateAtDatabaseBoundary()
    {
        await using var database = await Database.CreateAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = database.Open();
            var p = await new ParticipantIdentityService(db, Time).GetOrCreateAsync(12345, "player", "Игрок", null, default);
            await new ParticipantCollectionService(db).UpsertAsync(p.Id, [Item()], CollectionItemSource.Manual, Now, default);
        }));
        await using var verify = database.Open();
        var participant = await verify.Participants.SingleAsync();
        Assert.Single(await verify.ParticipantCollectionItems.ToArrayAsync());
        verify.Participants.Add(new() { TelegramUserId = participant.TelegramUserId, DisplayName = "Дубликат" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [PostgreSqlFact]
    public async Task ConfirmedImportReplaysFromNewScopeAfterExpiryWithoutApplyingNewSelection()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        Guid id;
        await using (var db = database.Open())
        {
            var job = new CampBggImport { ParticipantId = actor.Id, BggUsername = "owner", Status = CampBggImportStatus.Completed,
                ExpiresAt = Now.AddDays(1), DraftJson = CampBggImportDraftSerializer.Serialize(new(3, "owner", [Item(), Item(43)])) };
            db.Add(job); await db.SaveChangesAsync(); id = job.PublicId;
            Assert.Equal(1, (await Imports(db).ConfirmAsync(id, null, actor.Id, [42], [], default)).Added);
            job.ExpiresAt = Now.AddDays(-1); await db.SaveChangesAsync();
        }
        await using var replay = database.Open();
        Assert.True((await Imports(replay).ConfirmAsync(id, null, actor.Id, [43], [], default)).WasAlreadyConfirmed);
        Assert.Equal(42, (await replay.ParticipantCollectionItems.SingleAsync()).BggId);
    }

    [PostgreSqlFact]
    public async Task NotificationRecoveryReconsidersOnlyEligibleStatesAndConcurrentWorkersSendOnce()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        var counter = new Transport(); Guid gatheringId;
        await using (var db = database.Open())
        {
            var p = await db.Participants.SingleAsync(); p.PrivateChatStartedAt = Now;
            var g = GatheringRules.Create("club", Snapshot(), actor.Id, Now.AddMinutes(40), 1, 2, 4, null, false, Now);
            db.Add(g); db.NotificationPreferences.Add(new() { ParticipantId = p.Id, ReminderLeadMinutes = 60 });
            await db.SaveChangesAsync(); gatheringId = g.PublicId;
            await new GatheringReminderService(db, new(db, Time), Time).EnqueueDueAsync(default);
            (await db.Notifications.SingleAsync()).State = NotificationState.SuppressedByPreference;
            await db.SaveChangesAsync();
        }
        await using (var restore = database.Open())
            await new GatheringReminderService(restore, new(restore, Time), Time).EnqueueDueAsync(default);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        { await using var db = database.Open(); await new NotificationDispatcher(db, Time, counter).ProcessOneAsync(default); }));
        Assert.Equal(1, counter.Calls);
        await using (var states = database.Open())
        {
            foreach (var state in new[] { NotificationState.Failed, NotificationState.CannotMessageUser, NotificationState.DeliveryUnknown })
                states.Notifications.Add(new() { ParticipantId = actor.Id, Kind = NotificationKind.GatheringCancelled,
                    DeduplicationKey = state.ToString(), State = state, Text = "Отмена", NextAttemptAt = Now,
                    LastAttemptAt = Now.AddMinutes(-1), GatheringPublicId = gatheringId, CommunityKey = "club" });
            await states.SaveChangesAsync();
        }
        await using (var resumed = database.Open())
        { var dispatcher = new NotificationDispatcher(resumed, Time, counter); while (await dispatcher.ProcessOneAsync(default)) { } }
        Assert.Equal(3, counter.Calls);
        await using var verify = database.Open();
        Assert.Equal(3, await verify.Notifications.CountAsync(x => x.State == NotificationState.Delivered));
        Assert.Single(await verify.Notifications.Where(x => x.State == NotificationState.DeliveryUnknown).ToArrayAsync());
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [PostgreSqlFact]
    public async Task MigrationsBackfillPersonalOwnershipWithoutLosingCampCommitmentsOrRegistration()
    {
        await using var database = await Database.CreateAsync("20260903073138_AddGatheringGuests");
        await using var db = database.Open();
        var snapshot = CollectionItemSnapshotSerializer.Serialize(Item().Snapshot);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Participants" ("Id","TelegramUserId","DisplayName","CreatedAt","UpdatedAt") VALUES (1,12345,'Игрок',now(),now());
            INSERT INTO "OyinQCommunities" ("Key","Name","TelegramChatId","Mode","TimeZoneId","IsActive","CreatedAt","UpdatedAt")
            VALUES ('a','Кэмп A',-1001,1,'Asia/Almaty',true,now(),now()),('b','Кэмп B',-1002,1,'Asia/Almaty',true,now(),now());
            INSERT INTO "Camps" ("Id","BotChatKey","BotChatMode","Name","BaseCollectionJson","Status","StartDate","EndDate","CreatedByTelegramUserId","CreatedAt","UpdatedAt")
            VALUES (1,'a',1,'Кэмп A',jsonb_build_object('version',2,'games',jsonb_build_array()),1,'2026-09-04','2026-09-06',12345,now(),now()),
                   (2,'b',1,'Кэмп B',jsonb_build_object('version',2,'games',jsonb_build_array()),1,'2026-09-04','2026-09-06',12345,now(),now());
            INSERT INTO "CampRegistrations" ("Id","CampId","ParticipantId","DaysStaying","NeedsAccommodation","City","CreatedAt","UpdatedAt")
            VALUES (1,1,1,1,false,'Алматы',now(),now());
            INSERT INTO "CampRegistrationDays" ("CampRegistrationId","Date") VALUES (1,'2026-09-04');
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CampGameContributions" ("CampId","ParticipantId","BggId","ItemType","Source","Commitment","SnapshotJson","CreatedAt","UpdatedAt")
            VALUES (1,1,42,0,2,1,{snapshot}::jsonb,now(),now()),(2,1,42,0,1,0,{snapshot}::jsonb,now(),now());
            """);
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "Participants" SET "PreferredDisplayName" = 'Моё имя', "ActiveCommunityKey" = 'a';
            INSERT INTO "OyinQCommunities" ("Key","Name","TelegramChatId","Mode","TimeZoneId","IsActive","CreatedAt","UpdatedAt")
            VALUES ('club','Клуб',-1003,0,'UTC',true,now(),now());
            INSERT INTO "Clubs" ("Id","BotChatKey","BotChatMode","Name","CollectionJson","CollectionRevision","CreatedAt","UpdatedAt")
            VALUES (1,'club',0,'Клуб',jsonb_build_object('version',2,'games',jsonb_build_array(jsonb_build_object('bggId',42,'name','Игра'))),7,now(),now());
            UPDATE "Camps" SET "BaseCollectionJson" = (SELECT "CollectionJson" FROM "Clubs" WHERE "Id" = 1), "SourceClubId" = 1;
            INSERT INTO "GameGatherings" ("Id","PublicId","CommunityKey","GameSnapshotJson","OrganizerParticipantId","StartsAtUtc",
                "MinimumPlayers","DesiredPlayers","MaximumPlayers","CanTeachRules","Status","PublicationStatus","PublicationAttempts","CreatedAt","UpdatedAt")
            SELECT n,gen_random_uuid(),'a',jsonb_build_object('bggId',42,'name','Исторический снимок'),1,'2026-09-04 12:00:00+00',
                1,3,4,true,n,0,0,now(),now() FROM generate_series(1,5) n;
            INSERT INTO "GameGatheringParticipants" ("GameGatheringId","ParticipantId","Status","AttendanceOutcome","JoinedAt")
            SELECT "Id",1,0,0,now() FROM "GameGatherings";
            INSERT INTO "GameGatheringGuests" ("GameGatheringId","DisplayName","CreatedByParticipantId","CreatedAt","UpdatedAt")
            VALUES (1,'Гость',1,now(),now());
            INSERT INTO "CampBggImports" ("PublicId","CampId","ParticipantId","BggUsername","Status","ProgressCurrent","AttemptCount","CreatedAt","UpdatedAt","ExpiresAt")
            VALUES (gen_random_uuid(),1,1,'owner',0,0,0,now(),now(),now() + interval '1 day');
            """);
        // Compare every old column, including JSON, timestamps and foreign keys, after the upgrade.
        var preservedQueries = new[]
        {
            "SELECT to_jsonb(t)::text AS \"Value\" FROM \"Clubs\" t ORDER BY \"Id\"",
            "SELECT to_jsonb(t)::text AS \"Value\" FROM \"CampGameContributions\" t ORDER BY \"Id\"",
            "SELECT to_jsonb(t)::text AS \"Value\" FROM \"CampRegistrations\" t ORDER BY \"Id\"",
            "SELECT to_jsonb(t)::text AS \"Value\" FROM \"CampRegistrationDays\" t ORDER BY \"CampRegistrationId\",\"Date\"",
            "SELECT to_jsonb(t)::text AS \"Value\" FROM \"GameGatheringParticipants\" t ORDER BY \"Id\"",
            "SELECT to_jsonb(t)::text AS \"Value\" FROM \"CampBggImports\" t ORDER BY \"Id\"",
            "SELECT (to_jsonb(t) - ARRAY['ConfirmedWasPlayed','OutcomeRecordedAt','OutcomeRecordedByParticipantId','OutcomeRevision','LegacyPlayOutcomeJson'])::text AS \"Value\" FROM \"GameGatherings\" t ORDER BY \"Id\"",
            "SELECT (to_jsonb(t) - 'PublicId')::text AS \"Value\" FROM \"GameGatheringGuests\" t ORDER BY \"Id\"",
            "SELECT (to_jsonb(t) - ARRAY['PublicId','PrivateChatStartedAt','TelegramDeliveryBlockedAt'])::text AS \"Value\" FROM \"Participants\" t ORDER BY \"Id\"",
            "SELECT (to_jsonb(t) - ARRAY['StartDate','EndDate','StartsAtUtc','EndsAtUtc'])::text AS \"Value\" FROM \"Camps\" t ORDER BY \"Id\""
        };
        var before = new List<string[]>();
        foreach (var query in preservedQueries) before.Add(await db.Database.SqlQueryRaw<string>(query).ToArrayAsync());
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        for (var i = 0; i < preservedQueries.Length; i++)
            Assert.Equal(before[i], await db.Database.SqlQueryRaw<string>(preservedQueries[i]).ToArrayAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Null((await db.Participants.SingleAsync()).PrivateChatStartedAt);
        Assert.NotEqual(Guid.Empty, (await db.Participants.SingleAsync()).PublicId);
        Assert.NotEqual(Guid.Empty, (await db.GameGatheringGuests.SingleAsync()).PublicId);
        Assert.Empty(await db.GatheringPlayRecords.ToArrayAsync());
        Assert.Empty(await db.Notifications.ToArrayAsync());
        Assert.Empty(await db.ReleaseAnnouncements.ToArrayAsync());
        Assert.Single(await db.ParticipantCollectionItems.ToArrayAsync());
        Assert.Equal(CollectionItemSource.Manual, (await db.ParticipantCollectionItems.SingleAsync()).Source);
        Assert.Equal(new[] { CampBringCommitment.Bringing, CampBringCommitment.Available },
            await db.CampGameContributions.OrderBy(x => x.CampId).Select(x => x.Commitment).ToArrayAsync());
        Assert.Single(await db.CampRegistrations.Include(x => x.SelectedDays).ToArrayAsync());
        Assert.Single((await db.CampRegistrations.Include(x => x.SelectedDays).SingleAsync()).SelectedDays);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 19, 0, 0, TimeSpan.Zero), (await db.Camps.FirstAsync()).StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 19, 0, 0, TimeSpan.Zero), (await db.Camps.FirstAsync()).EndsAtUtc);
    }

    [PostgreSqlFact]
    public async Task ReleaseRecoveryAcrossScopesPreservesDeliveredAndUnknownAndRetriesOnlyFailed()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        var sends = new ReleaseSender();
        using var http = new HttpClient(new ReleaseBot());
        var bot = new Telegram.Bot.TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", http);
        ReleaseAnnouncementService Service(AppDbContext db) => new(db,
            new AdminAuthorizationService(db, null!, Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = new HashSet<long> { actor.TelegramUserId } }), Time),
            bot, sends, Time);
        await using (var db = database.Open())
        {
            await Service(db).QueueAsync(actor.TelegramUserId, ReleaseContent.Id, ["club"], true, false, default);
        }
        sends.FailPreparation = true;
        await using (var db = database.Open()) await Service(db).DispatchOneAsync(default);
        Assert.Equal(0, sends.Calls);
        sends.FailPreparation = false;
        await using (var db = database.Open())
        {
            Assert.Equal(ReleaseDeliveryState.Failed, (await db.ReleaseAnnouncementDeliveries.SingleAsync()).State);
            await Service(db).QueueAsync(actor.TelegramUserId, ReleaseContent.Id, ["club"], true, true, default);
        }
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ => { await using var db = database.Open(); await Service(db).DispatchOneAsync(default); }));
        Assert.Equal(1, sends.Calls);
        await using (var db = database.Open())
        {
            Assert.Equal(ReleaseDeliveryState.Delivered, (await db.ReleaseAnnouncementDeliveries.SingleAsync()).State);
            await Service(db).QueueAsync(actor.TelegramUserId, ReleaseContent.Id, ["club"], true, true, default);
            Assert.False(await Service(db).DispatchOneAsync(default));
            var row = await db.ReleaseAnnouncementDeliveries.SingleAsync();
            row.State = ReleaseDeliveryState.Delivering; row.AttemptedAt = Now.AddMinutes(-10); await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
        {
            Assert.False(await Service(db).DispatchOneAsync(default));
            Assert.Equal(ReleaseDeliveryState.DeliveryUnknown, (await db.ReleaseAnnouncementDeliveries.SingleAsync()).State);
            await Service(db).QueueAsync(actor.TelegramUserId, ReleaseContent.Id, ["club"], true, true, default);
            Assert.False(await Service(db).DispatchOneAsync(default));
        }
        Assert.Equal(1, sends.Calls);
        await using (var interrupted = database.Open())
        {
            var row = await interrupted.ReleaseAnnouncementDeliveries.SingleAsync();
            row.State = ReleaseDeliveryState.Preparing; row.AttemptedAt = Now.AddMinutes(-10);
            await interrupted.SaveChangesAsync();
        }
        await using (var recovered = database.Open())
        {
            Assert.False(await Service(recovered).DispatchOneAsync(default));
            Assert.Equal(ReleaseDeliveryState.Failed, (await recovered.ReleaseAnnouncementDeliveries.SingleAsync()).State);
        }
        Assert.Equal(1, sends.Calls);
    }

    private sealed class ReleaseSender : ITelegramGroupMessageSender
    {
        public int Calls; public bool FailPreparation;
        public Task<Func<CancellationToken, Task<Telegram.Bot.Types.Message>>> PrepareMessageAsync(string key, string text,
            Telegram.Bot.Types.Enums.ParseMode mode, Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? markup, CancellationToken ct)
        {
            if (FailPreparation) throw new HttpRequestException("preparation");
            return Task.FromResult<Func<CancellationToken, Task<Telegram.Bot.Types.Message>>>(token => SendMessageAsync(key, text, mode, markup, token));
        }
        public Task<Telegram.Bot.Types.Message> SendMessageAsync(string key, string text, Telegram.Bot.Types.Enums.ParseMode mode,
            Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? markup, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return Task.FromResult(new Telegram.Bot.Types.Message { Id = 123 }); }
        public Task<Telegram.Bot.Types.Message> SendPhotoAsync(string key, Telegram.Bot.Types.InputFile photo, string caption,
            Telegram.Bot.Types.Enums.ParseMode mode, Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? markup, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class ReleaseBot : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var result = request.RequestUri!.AbsolutePath.Split('/').Last().ToLowerInvariant() switch
            {
                "getme" => """{"id":123456,"is_bot":true,"first_name":"OyinQ","username":"TestBot"}""",
                "getchatmember" => """{"status":"creator","user":{"id":123456,"is_bot":true,"first_name":"OyinQ"},"is_anonymous":false}""",
                "getchat" => """{"id":-10012345,"type":"supergroup","title":"Клуб","accent_color_id":0,"max_reaction_count":1}""",
                _ => throw new InvalidOperationException("Внешняя отправка запрещена тестом.")
            };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("{\"ok\":true,\"result\":" + result + "}", System.Text.Encoding.UTF8, "application/json") });
        }
    }
    private static GatheringGameSnapshot Snapshot() => new(GatheringGameSnapshot.CurrentVersion, 42, "Игра", null, null, 1, 4, null, [], "bgg", []);
    private static CreateGatheringCommand Command() => new("club", "bgg", 42, [], Now.AddHours(1), 1, 2, 4, null, false, true);
    private static GatheringManagementService Management(AppDbContext db, Bgg bgg) =>
        new(db, new(db, bgg), new(db, Time), new(db, new NotificationService(db, Time)), Time);
    private static CampBggImportCoordinator Imports(AppDbContext db) => new(db, new(db, new(db, Time), Time), new(db, Time), Time);
    private static CommunityDeletionService Deletion(AppDbContext db, long userId) => new(db,
        new AdminAuthorizationService(db, null!, Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = new HashSet<long> { userId } }), Time), Time);
    private static async Task<Participant> SeedAsync(Database database, bool camp = false)
    {
        await using var db = database.Open();
        var p = await new ParticipantIdentityService(db, Time).GetOrCreateAsync(12345, null, "Игрок", null, default);
        var community = new OyinQCommunity { Key = "club", Name = "Сообщество", Mode = camp ? BotMode.Camp : BotMode.Club,
            TelegramChatId = -10012345, TimeZoneId = "UTC", IsActive = true };
        if (camp)
        {
            var c = new Camp { BotChat = community, Status = CampStatus.Active, StartsAtUtc = Now.AddDays(-1), EndsAtUtc = Now.AddDays(1),
                BaseCollectionJson = ClubCollectionSerializer.Serialize(new(2, [])) };
            db.Camps.Add(c); db.CampRegistrations.Add(new() { Camp = c, ParticipantId = p.Id, City = "Алматы", NeedsAccommodation = false,
                SelectedDays = [new() { Date = DateOnly.FromDateTime(Now.UtcDateTime) }] });
        }
        else db.Clubs.Add(new() { BotChat = community, CollectionJson = ClubCollectionSerializer.Serialize(new(2, [])) });
        await db.SaveChangesAsync(); return p;
    }
    private sealed class Bgg(Func<Task>? lookup = null) : IBoardGameGeekClient
    {
        public async Task<BggGameDetails?> GetGameDetailsAsync(long id, CancellationToken ct)
        { if (lookup is not null) await lookup(); return new(new ExternalGame(42, "Игра", 1, 4, null, "https://boardgamegeek.com/boardgame/42"), []); }
        public Task<IReadOnlyList<BggBaseGameSearchResult>> SearchAsync(string q, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Transport : INotificationTransport
    {
        public int Calls;
        public Task<NotificationReceipt> SendAsync(Notification n, Participant p, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return Task.FromResult(new NotificationReceipt(123)); }
    }
    private sealed class SaveGate(TaskCompletionSource entered, TaskCompletionSource resume) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<GameGathering>().Any(x => x.State == EntityState.Added))
            { entered.TrySetResult(); await resume.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); }
            return result;
        }
    }
    private sealed class LockObserver(TaskCompletionSource entered) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("OyinQCommunities") && command.CommandText.Contains("FOR UPDATE")) entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
    private sealed class RejectGathering : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<GameGathering>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Проверка отката после сохранения коллекции и вклада.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Database(string adminConnection, string name) : IAsyncDisposable
    {
        public AppDbContext Open(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(adminConnection) { Database = name, Pooling = false }.ConnectionString)
            .AddInterceptors(interceptors).Options);
        public static async Task<Database> CreateAsync(string? migration = null)
        {
            var connection = Environment.GetEnvironmentVariable("OYINQ_TEST_POSTGRES")
                ?? throw new InvalidOperationException("Тестовый PostgreSQL не настроен.");
            var name = "oyinq_test_" + Guid.NewGuid().ToString("N");
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", admin); await command.ExecuteNonQueryAsync();
            var result = new Database(connection, name);
            try
            {
                await using var db = result.Open(); await db.GetService<IMigrator>().MigrateAsync(migration); return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await command.ExecuteNonQueryAsync();
        }
    }
}
