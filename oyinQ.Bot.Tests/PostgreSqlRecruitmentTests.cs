using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task WishlistUpgradePreservesPreferencesEnablesNewDefaultAndReplays()
    {
        await using var database = await Database.CreateAsync("20260904092743_PlayOutcomesReferencesAndReleases");
        await using var db = database.Open();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Participants" ("Id","TelegramUserId","DisplayName","CreatedAt","UpdatedAt") VALUES (1,12345,'Игрок',now(),now());
            INSERT INTO "OyinQCommunities" ("Key","Name","TelegramChatId","Mode","TimeZoneId","IsActive","CreatedAt","UpdatedAt")
            VALUES ('club','Клуб',-1001,0,'UTC',true,now(),now());
            INSERT INTO "NotificationPreferences" ("ParticipantId","GatheringFull","GatheringDetailsChanged","OrganizerParticipantLeft",
                "OrganizerReplacement","OrganizerBelowMinimum","OrganizerMissingProvider","ImportCompleted","ReminderLeadMinutes")
            VALUES (1,true,false,false,true,false,true,false,120);
            """);
        await db.GetService<IMigrator>().MigrateAsync(); await db.GetService<IMigrator>().MigrateAsync();
        var preferences = await db.NotificationPreferences.SingleAsync();
        Assert.True(preferences.WishlistGathering); Assert.True(preferences.GatheringFull); Assert.False(preferences.GatheringDetailsChanged);
        Assert.Equal(120, preferences.ReminderLeadMinutes);
        var community = await db.OyinQCommunities.SingleAsync();
        Assert.Equal(4, community.RecruitmentCooldownHours); Assert.Null(community.LastRecruitmentDigestAt);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlFact]
    public async Task ConcurrentWishesAndDigestRequestsAreAtomicAndWorkersSendOnce()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = database.Open();
            var catalog = new GameCatalogService(db, new(db, new(db, new(db, Time), Time)), Time);
            await new GameWishService(db, catalog, new Bgg(), Time).SetAsync("club", actor.Id, 42, true, default);
        }));
        Guid id;
        await using (var db = database.Open())
        {
            Assert.Single(await db.GameWishes.ToArrayAsync());
            Assert.Empty(await db.ParticipantCollectionItems.ToArrayAsync());
            var g = await Management(db, new Bgg()).CreateAsync((await db.OyinQCommunities.SingleAsync()).ToBotCommunity(),
                new(actor.TelegramUserId, null, "Игрок"), Command(), default);
            id = g.PublicId;
        }
        var requests = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = database.Open();
            return await new RecruitmentDigestService(db, Time).RequestAsync("club", id, actor.Id, default);
        }));
        Assert.Single(requests, x => x.Queued);
        using var http = new HttpClient(new ReleaseBot());
        var bot = new Telegram.Bot.TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", http);
        var sender = new ReleaseSender();
        var dispatchClock = new PlanningClock { Now = Now.AddMinutes(3) };
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = database.Open();
            await new RecruitmentDigestDispatcher(db, dispatchClock, new(db, dispatchClock), sender, bot,
                NullLogger<RecruitmentDigestDispatcher>.Instance).ProcessOneAsync(default);
        }));
        Assert.Equal(1, sender.Calls);
        await using var verify = database.Open();
        Assert.Equal(RecruitmentDigestState.Delivered, (await verify.RecruitmentDigests.SingleAsync()).State);
        Assert.Equal(dispatchClock.Now.AddHours(4), RecruitmentDigestService.AvailableAt(await verify.OyinQCommunities.SingleAsync()));
        Assert.False((await new RecruitmentDigestService(verify, Time).RequestAsync("club", id, actor.Id, default)).Queued);
    }

    [PostgreSqlFact]
    public async Task CampWishCreatesWithoutCopyAndLaterBringingPreservesGathering()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database, camp: true);
        await using var db = database.Open();
        var policy = new Features.Communities.CampParticipationPolicy(db, Time);
        var contributions = new CampContributionSelectionService(db, policy, Time);
        var effective = new EffectiveCampCatalogService(db, contributions); var catalog = new GameCatalogService(db, effective, Time);
        await new GameWishService(db, catalog, new Bgg(), Time).SetAsync("club", actor.Id, 42, true, default);
        var management = new GatheringManagementService(db, new(db, new Bgg(), effective), policy, new(db, new(db, Time)), Time);
        var g = await management.CreateAsync((await db.OyinQCommunities.SingleAsync()).ToBotCommunity(), new(actor.TelegramUserId, null, "Игрок"),
            Command() with { GameSource = "catalog" }, default);
        var providers = new GameProviderService(db, catalog, effective, policy, contributions, Time);
        Assert.False((await providers.ForGatheringAsync(g, actor.Id, default)).IsConfirmed);
        var id = g.PublicId; var snapshot = g.GameSnapshotJson; var start = g.StartsAtUtc;
        g.TelegramChatId = -10012345; g.TelegramMessageId = 999;
        await db.SaveChangesAsync();
        await new ParticipantCollectionService(db).UpsertAsync(actor.Id, [Item()], CollectionItemSource.Manual, Now, default);
        var campId = await db.Camps.Select(x => x.Id).SingleAsync();
        await contributions.SetCommitmentAsync(campId, actor.Id, 42, CollectionItemType.BaseGame, CampBringCommitment.Bringing, default, start);
        Assert.True((await providers.ForGatheringAsync(g, actor.Id, default)).IsConfirmed);
        var saved = await db.GameGatherings.SingleAsync();
        Assert.Equal(id, saved.PublicId); Assert.Equal(snapshot, saved.GameSnapshotJson); Assert.Equal(start, saved.StartsAtUtc);
        Assert.Equal(999, saved.TelegramMessageId); Assert.Single(await db.GameWishes.ToArrayAsync());
        Assert.Empty(await db.Notifications.Where(x => x.Kind == NotificationKind.WishlistGathering).ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task DigestPreparationFailureReleasesCooldownButUncertainSendDoesNotRetry()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        using var http = new HttpClient(new ReleaseBot());
        var bot = new Telegram.Bot.TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", http);
        var sender = new ReleaseSender { FailPreparation = true }; Guid id;
        await using (var db = database.Open())
        {
            id = (await Management(db, new Bgg()).CreateAsync((await db.OyinQCommunities.SingleAsync()).ToBotCommunity(),
                new(actor.TelegramUserId, null, "Игрок"), Command(), default)).PublicId;
            await new RecruitmentDigestService(db, Time).RequestAsync("club", id, actor.Id, default);
        }
        await using (var db = database.Open())
            await new RecruitmentDigestDispatcher(db, Time, new(db, Time), sender, bot,
                NullLogger<RecruitmentDigestDispatcher>.Instance).ProcessOneAsync(default);
        Assert.Equal(0, sender.Calls);
        await using (var db = database.Open())
        {
            Assert.Equal(RecruitmentDigestState.Failed, (await db.RecruitmentDigests.SingleAsync()).State);
            Assert.True((await new RecruitmentDigestService(db, Time).RequestAsync("club", id, actor.Id, default)).Queued);
            var row = await db.RecruitmentDigests.SingleAsync(x => x.State == RecruitmentDigestState.Pending);
            row.State = RecruitmentDigestState.Delivering; row.LeaseExpiresAt = Now.AddMinutes(-1); await db.SaveChangesAsync();
        }
        sender.FailPreparation = false;
        await using (var db = database.Open())
        {
            var dispatcher = new RecruitmentDigestDispatcher(db, Time, new(db, Time), sender, bot, NullLogger<RecruitmentDigestDispatcher>.Instance);
            Assert.True(await dispatcher.ProcessOneAsync(default)); Assert.False(await dispatcher.ProcessOneAsync(default));
            Assert.Single(await db.RecruitmentDigests.Where(x => x.State == RecruitmentDigestState.DeliveryUnknown).ToArrayAsync());
            Assert.False((await new RecruitmentDigestService(db, Time).RequestAsync("club", id, actor.Id, default)).Queued);
        }
        Assert.Equal(0, sender.Calls);
    }

    [PostgreSqlFact]
    public async Task ReclaimedDigestPreparationCannotSendWithAnOldAttempt()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        using var http = new HttpClient(new ReleaseBot());
        var bot = new Telegram.Bot.TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", http);
        var sender = new PausedDigestSender();
        await using (var db = database.Open())
        {
            var g = await Management(db, new Bgg()).CreateAsync((await db.OyinQCommunities.SingleAsync()).ToBotCommunity(),
                new(actor.TelegramUserId, null, "Игрок"), Command(), default);
            await new RecruitmentDigestService(db, Time).RequestAsync("club", g.PublicId, actor.Id, default);
        }
        await using var first = database.Open();
        var oldAttempt = new RecruitmentDigestDispatcher(first, Time, new(first, Time), sender, bot,
            NullLogger<RecruitmentDigestDispatcher>.Instance).ProcessOneAsync(default);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await using (var expire = database.Open())
                await expire.RecruitmentDigests.ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAt, Now.AddMinutes(-1)));
            await using var second = database.Open();
            await new RecruitmentDigestDispatcher(second, Time, new(second, Time), sender, bot,
                NullLogger<RecruitmentDigestDispatcher>.Instance).ProcessOneAsync(default);
        }
        finally { sender.Resume.TrySetResult(); }
        await oldAttempt.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(1, sender.Sends);
        await using var verify = database.Open();
        Assert.Equal(RecruitmentDigestState.Delivered, (await verify.RecruitmentDigests.SingleAsync()).State);
    }

    [PostgreSqlFact]
    public async Task DigestDoesNotSendGatheringsCancelledDuringPreparation()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        using var http = new HttpClient(new ReleaseBot());
        var bot = new Telegram.Bot.TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", http);
        var sender = new PausedDigestSender();
        await using (var db = database.Open())
        {
            var g = await Management(db, new Bgg()).CreateAsync((await db.OyinQCommunities.SingleAsync()).ToBotCommunity(),
                new(actor.TelegramUserId, null, "Игрок"), Command(), default);
            await new RecruitmentDigestService(db, Time).RequestAsync("club", g.PublicId, actor.Id, default);
        }
        await using var first = database.Open();
        var sending = new RecruitmentDigestDispatcher(first, Time, new(first, Time), sender, bot,
            NullLogger<RecruitmentDigestDispatcher>.Instance).ProcessOneAsync(default);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await using var cancel = database.Open();
            await cancel.GameGatherings.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GatheringStatus.Cancelled));
        }
        finally { sender.Resume.TrySetResult(); }
        await sending.WaitAsync(TimeSpan.FromSeconds(15));
        await using var second = database.Open();
        await new RecruitmentDigestDispatcher(second, Time, new(second, Time), sender, bot,
            NullLogger<RecruitmentDigestDispatcher>.Instance).ProcessOneAsync(default);
        Assert.Equal(0, sender.Sends);
        Assert.Equal(RecruitmentDigestState.Expired, (await second.RecruitmentDigests.SingleAsync()).State);
    }

    private sealed class PausedDigestSender : Integrations.Telegram.ITelegramGroupMessageSender
    {
        public TaskCompletionSource Entered { get; } = Signal();
        public TaskCompletionSource Resume { get; } = Signal();
        private int preparations;
        public int Sends;
        public async Task<Func<CancellationToken, Task<Telegram.Bot.Types.Message>>> PrepareMessageAsync(string key, string text,
            Telegram.Bot.Types.Enums.ParseMode mode, Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? markup, CancellationToken ct)
        {
            if (Interlocked.Increment(ref preparations) == 1)
            { Entered.TrySetResult(); await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
            return token => SendMessageAsync(key, text, mode, markup, token);
        }
        public Task<Telegram.Bot.Types.Message> SendMessageAsync(string key, string text, Telegram.Bot.Types.Enums.ParseMode mode,
            Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? markup, CancellationToken ct)
        { Interlocked.Increment(ref Sends); return Task.FromResult(new Telegram.Bot.Types.Message { Id = 321 }); }
        public Task<Telegram.Bot.Types.Message> SendPhotoAsync(string key, Telegram.Bot.Types.InputFile photo, string caption,
            Telegram.Bot.Types.Enums.ParseMode mode, Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? markup, CancellationToken ct) => throw new NotSupportedException();
    }
}
