using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class ClubRevisionTests
{
    [Fact]
    public void StaleCollectionResponse_PreservesConflictContract()
    {
        var result = Features.MiniApp.MiniAppEndpointSupport.FromException(new ClubCollectionConflictException(617));
        Assert.Equal(409, Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result).StatusCode);
        var value = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(result).Value;
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(value);
        Assert.Equal("stale_revision", payload.GetProperty("code").GetString());
        Assert.Equal(617, payload.GetProperty("currentRevision").GetInt64());
    }

    [Fact]
    public void Equality_UpgradesLegacyJsonAndIgnoresObjectAndMembershipOrdering()
    {
        var original = """{"games":[{"name":"B","bggId":2},{"name":"A","bggId":1,"expansions":[{"name":"Y","bggId":12},{"name":"X","bggId":11}]}],"version":1}""";
        var club = new Club { CollectionJson = original, CollectionRevision = 617, UpdatedAt = DateTimeOffset.UnixEpoch };
        var document = club.ReadCollection();
        var reordered = document with { Games = document.Games.Reverse().Select(game => game with { Expansions = game.Expansions.Reverse().ToArray() }).ToArray() };
        Assert.False(club.ReplaceCollection(reordered, DateTimeOffset.UtcNow));
        Assert.Equal(617, club.CollectionRevision);
        Assert.Equal(original, club.CollectionJson);
        Assert.Equal(DateTimeOffset.UnixEpoch, club.UpdatedAt);
        Assert.True(club.ReplaceCollection(reordered with { Games = reordered.Games.Select(game => game with { Description = "New" }).ToArray() }, DateTimeOffset.UtcNow));
        Assert.Equal(618, club.CollectionRevision);
    }
}

public sealed partial class PostgreSqlStabilizationTests
{
    private static ClubCollectionGame RevisionGame(long id, string? name = null) =>
        new(id, name ?? $"Game {id}", null, null, 2, 4, null, []);
    private static BggGameDetails RevisionDetails(long id, string? name = null) =>
        new(new ExternalGame(id, name ?? $"Fresh {id}", 2, 4, null, null), [new(11, "Fresh expansion 11"), new(12, "Fresh expansion 12"), new(13, "Fresh expansion 13")]);

    private static async Task<long> SeedRevisionClub(Database database, params ClubCollectionGame[] games)
    {
        await SeedAsync(database);
        await using var db = database.Open(); var club = await db.Clubs.SingleAsync();
        club.CollectionJson = ClubCollectionSerializer.Serialize(new(2, games)); club.CollectionRevision = 10;
        await db.SaveChangesAsync(); return club.Id;
    }
    private static async Task<ClubMetadataRefreshView> QueueRevisionRefresh(Database database, long clubId)
    {
        await using var db = database.Open();
        return await new ClubMetadataRefreshService(db, new RevisionBgg(), Time).QueueAsync(clubId, default);
    }
    private static async Task ProcessRevisionRefresh(Database database, RevisionBgg? provider = null)
    {
        // A fresh context for every item proves persistence across worker/process lifetimes.
        await using var db = database.Open();
        Assert.True(await new ClubMetadataRefreshService(db, provider ?? new RevisionBgg(), Time).ProcessOneAsync(default));
    }

    [PostgreSqlFact]
    public async Task MetadataRefresh_PublishesThreeGamesOnce_AndIdenticalRetryIsNoOp()
    {
        await using var database = await Database.CreateAsync();
        var clubId = await SeedRevisionClub(database, RevisionGame(1), RevisionGame(2), RevisionGame(3));
        await QueueRevisionRefresh(database, clubId);
        for (var progress = 1; progress <= 3; progress++)
        {
            await ProcessRevisionRefresh(database);
            await using var db = database.Open();
            var club = await db.Clubs.SingleAsync(); var job = await db.ClubMetadataRefreshes.SingleAsync();
            Assert.Equal(progress, job.ProgressCurrent);
            Assert.Equal(progress, ClubCollectionSerializer.Deserialize(job.StagedCollectionJson).Games.Count);
            Assert.Equal(progress < 3 ? 10 : 11, club.CollectionRevision);
            Assert.All(club.ReadCollection().Games, game => Assert.StartsWith(progress < 3 ? "Game" : "Fresh", game.Name));
            Assert.Equal(progress < 3 ? ClubMetadataRefreshStatus.Queued : ClubMetadataRefreshStatus.Completed, job.Status);
        }
        await QueueRevisionRefresh(database, clubId);
        for (var i = 0; i < 3; i++) await ProcessRevisionRefresh(database);
        await using var final = database.Open();
        Assert.Equal(11, (await final.Clubs.SingleAsync()).CollectionRevision);
        var jobs = await final.ClubMetadataRefreshes.OrderBy(job => job.Id).ToArrayAsync();
        Assert.Equal(3, jobs[0].UpdatedGames); Assert.Equal(0, jobs[1].UpdatedGames);
    }

    [PostgreSqlFact]
    public async Task MetadataRefresh_ReusedWorkerScopeReadsOtherWorkersProgress()
    {
        await using var database = await Database.CreateAsync();
        var clubId = await SeedRevisionClub(database, RevisionGame(1), RevisionGame(2), RevisionGame(3));
        await QueueRevisionRefresh(database, clubId);
        await using var workerContext = database.Open();
        var requested = new List<long>();
        var worker = new ClubMetadataRefreshService(workerContext,
            new RevisionBgg(id => { requested.Add(id); return RevisionDetails(id); }), Time);
        Assert.True(await worker.ProcessOneAsync(default));
        await ProcessRevisionRefresh(database);
        Assert.True(await worker.ProcessOneAsync(default));
        Assert.Equal([1L, 3L], requested);
        await using var final = database.Open();
        Assert.Equal(11, (await final.Clubs.SingleAsync()).CollectionRevision);
        Assert.Equal(ClubMetadataRefreshStatus.Completed, (await final.ClubMetadataRefreshes.SingleAsync()).Status);
    }

    [PostgreSqlFact]
    public async Task MetadataRefresh_FailureDoesNotPublishStagedGames_AndExplicitRetryCanComplete()
    {
        await using var database = await Database.CreateAsync();
        var clubId = await SeedRevisionClub(database, RevisionGame(1), RevisionGame(2));
        await QueueRevisionRefresh(database, clubId); await ProcessRevisionRefresh(database);
        await ProcessRevisionRefresh(database, new RevisionBgg(_ => throw new HttpRequestException("fake BGG failure")));
        await using (var db = database.Open())
        {
            var club = await db.Clubs.SingleAsync(); var job = await db.ClubMetadataRefreshes.SingleAsync();
            Assert.Equal(10, club.CollectionRevision); Assert.All(club.ReadCollection().Games, game => Assert.StartsWith("Game", game.Name));
            Assert.Equal(ClubMetadataRefreshStatus.Failed, job.Status); Assert.Equal(1, job.ProgressCurrent);
            Assert.Single(ClubCollectionSerializer.Deserialize(job.StagedCollectionJson).Games);
        }
        await QueueRevisionRefresh(database, clubId);
        await ProcessRevisionRefresh(database); await ProcessRevisionRefresh(database);
        await using var final = database.Open();
        Assert.Equal(11, (await final.Clubs.SingleAsync()).CollectionRevision);
    }

    [PostgreSqlFact]
    public async Task MetadataRefresh_MergesCurrentMembershipAndExpansions_AndLinkedCampSeesOnlyPublication()
    {
        await using var database = await Database.CreateAsync();
        var clubId = await SeedRevisionClub(database, RevisionGame(1) with { Expansions = [new(11, "Old 11"), new(12, "Old 12")] }, RevisionGame(2));
        await using (var db = database.Open())
        {
            db.Camps.Add(new Camp { BotChat = new() { Key = "linked-camp", Name = "Camp", Mode = Common.Options.BotMode.Camp, TelegramChatId = -100999, TimeZoneId = "UTC" }, SourceClubId = clubId });
            db.Clubs.Add(new Club { BotChat = new() { Key = "linked-club", Name = "Linked", Mode = Common.Options.BotMode.Club, TelegramChatId = -100998, TimeZoneId = "UTC" }, SourceClubId = clubId });
            await db.SaveChangesAsync();
        }
        await QueueRevisionRefresh(database, clubId); await ProcessRevisionRefresh(database);
        await using (var edit = database.Open())
        {
            var service = new ClubCollectionService(edit);
            var a = (await service.GetAsync(clubId, default)).Collection.Games.Single(game => game.BggId == 1);
            await service.RemoveGameAsync(clubId, 2, 10, Now, default);
            await service.AddOrReplaceGameAsync(clubId, RevisionGame(3, "Admin added C"), 11, Now, default);
            await service.AddOrReplaceGameAsync(clubId, a with { Expansions = [new(12, "Old 12"), new(13, "Admin 13")] }, 12, Now, default);
            var before = await new SharedCollectionReader(edit).ForCampAsync(await edit.Camps.SingleAsync(), default);
            Assert.Equal("Game 1", before.Games.Single(game => game.BggId == 1).Name);
        }
        await ProcessRevisionRefresh(database);
        await using var final = database.Open();
        var camp = await new SharedCollectionReader(final).ForCampAsync(await final.Camps.SingleAsync(), default);
        Assert.Equal([1L, 3L], camp.Games.Select(game => game.BggId).Order());
        var enriched = camp.Games.Single(game => game.BggId == 1);
        Assert.Equal("Fresh 1", enriched.Name); Assert.Equal([12L, 13L], enriched.Expansions.Select(x => x.BggId));
        Assert.Equal("Fresh expansion 13", enriched.Expansions[1].Name);
        Assert.Equal("Admin added C", camp.Games.Single(game => game.BggId == 3).Name);
        Assert.Equal(14, (await final.Clubs.SingleAsync(club => club.Id == clubId)).CollectionRevision);
        var linked = await new ClubCollectionService(final).GetAsync((await final.Clubs.SingleAsync(club => club.SourceClubId != null)).Id, default);
        Assert.Equal(14, linked.Revision); Assert.Equal(clubId, linked.SourceClubId); Assert.False(linked.CanEdit);
    }

    [PostgreSqlFact]
    public async Task CollectionMutations_OnlyEffectiveWritesIncrementRevision_StaleNoOpStillConflicts()
    {
        await using var database = await Database.CreateAsync();
        var clubId = await SeedRevisionClub(database);
        await using var db = database.Open(); var service = new ClubCollectionService(db);
        var game = RevisionGame(1);
        await service.AddOrReplaceGameAsync(clubId, game, 10, Now, default);
        await service.AddOrReplaceGameAsync(clubId, game, 11, Now, default);
        Assert.Equal(11, (await service.GetAsync(clubId, default)).Revision);
        await Assert.ThrowsAsync<ClubCollectionConflictException>(() => service.AddOrReplaceGameAsync(clubId, game, 10, Now, default));
        var expanded = game with { Expansions = [new(11, "Expansion")] };
        await service.AddOrReplaceGameAsync(clubId, expanded, 11, Now, default);
        await service.AddOrReplaceGameAsync(clubId, expanded, 12, Now, default);
        await service.ReplaceAsync(clubId, new(2, [expanded]), 12, Now, default);
        Assert.False(await service.RemoveGameAsync(clubId, 999, 12, Now, default));
        Assert.True(await service.RemoveGameAsync(clubId, 1, 12, Now, default));
        Assert.Equal(13, (await service.GetAsync(clubId, default)).Revision);
        await service.ReplaceAsync(clubId, new(2, [game, RevisionGame(2)]), 13, Now, default);
        var final = await service.GetAsync(clubId, default);
        var exported = ClubCollectionSerializer.Serialize(final.Collection);
        await service.ReplaceAsync(clubId, ClubCollectionSerializer.Deserialize(exported), 14, Now, default);
        Assert.Equal(14, (await service.GetAsync(clubId, default)).Revision);
    }

    [PostgreSqlFact]
    public async Task MetadataRefresh_ExpiredLeaseCannotPublishAfterReplacementWorker()
    {
        await using var database = await Database.CreateAsync();
        var clubId = await SeedRevisionClub(database, RevisionGame(1)); await QueueRevisionRefresh(database, clubId);
        var entered = Signal(); var resume = Signal();
        await using var staleDb = database.Open();
        var stale = new ClubMetadataRefreshService(staleDb, new RevisionBgg(asyncLoad: async id =>
        { entered.TrySetResult(); await resume.Task; return RevisionDetails(id, "Stale worker"); }), Time).ProcessOneAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using (var db = database.Open())
            { var job = await db.ClubMetadataRefreshes.SingleAsync(); job.LeaseExpiresAt = Now.AddMinutes(-1); await db.SaveChangesAsync(); }
            await ProcessRevisionRefresh(database);
        }
        finally { resume.TrySetResult(); }
        await stale;
        await using var final = database.Open();
        var club = await final.Clubs.SingleAsync(); Assert.Equal(11, club.CollectionRevision);
        Assert.Equal("Fresh 1", club.ReadCollection().Games.Single().Name);
        Assert.Equal(ClubMetadataRefreshStatus.Completed, (await final.ClubMetadataRefreshes.SingleAsync()).Status);
    }

    [PostgreSqlFact]
    public async Task MetadataRefresh_PublicationFailureRollsBackCollectionAndRevision()
    {
        await using var database = await Database.CreateAsync();
        var clubId = await SeedRevisionClub(database, RevisionGame(1), RevisionGame(2));
        await QueueRevisionRefresh(database, clubId); await ProcessRevisionRefresh(database);
        await using (var worker = database.Open(new RejectClubPublication()))
            Assert.True(await new ClubMetadataRefreshService(worker, new RevisionBgg(), Time).ProcessOneAsync(default));
        await using var final = database.Open(); var club = await final.Clubs.SingleAsync();
        Assert.Equal(10, club.CollectionRevision); Assert.All(club.ReadCollection().Games, game => Assert.StartsWith("Game", game.Name));
        var job = await final.ClubMetadataRefreshes.SingleAsync(); Assert.Equal(ClubMetadataRefreshStatus.Failed, job.Status);
        Assert.Equal(1, job.ProgressCurrent); Assert.Null(job.UpdatedGames);
    }

    [PostgreSqlFact]
    public async Task MetadataRefresh_LegacyRunningJobRestartsStagingWithoutResettingRevision()
    {
        await using var database = await Database.CreateAsync("20260916104537_SharedClubCollections");
        // Seed with SQL because the old schema intentionally has no staging columns.
        await SeedAsync(database);
        await using (var db = database.Open())
        {
            var club = await db.Clubs.SingleAsync(); club.CollectionRevision = 617;
            club.CollectionJson = ClubCollectionSerializer.Serialize(new(2, [RevisionGame(1), RevisionGame(2)]));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "ClubMetadataRefreshes" ("PublicId","ClubId","Status","BggIdsJson","ProgressCurrent","ProgressTotal","CreatedAt","UpdatedAt")
                VALUES ({{Guid.NewGuid()}},{{club.Id}},0,'[1,2]',1,2,{{Now}},{{Now}})
                """);
            await db.Database.MigrateAsync();
        }
        await ProcessRevisionRefresh(database);
        await using (var db = database.Open()) { Assert.Equal(617, (await db.Clubs.SingleAsync()).CollectionRevision); Assert.Equal(1, (await db.ClubMetadataRefreshes.SingleAsync()).ProgressCurrent); }
        await ProcessRevisionRefresh(database);
        await using var final = database.Open(); Assert.Equal(618, (await final.Clubs.SingleAsync()).CollectionRevision);
    }

    private sealed class RejectClubPublication : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<Club>().Any(entry => entry.State == EntityState.Modified))
                throw new InvalidOperationException("Simulated publication failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RevisionBgg(Func<long, BggGameDetails?>? load = null, Func<long, Task<BggGameDetails?>>? asyncLoad = null) : IBoardGameGeekClient
    {
        public Task<BggGameDetails?> GetGameDetailsAsync(long id, CancellationToken ct) => asyncLoad is null
            ? Task.FromResult(load is null ? RevisionDetails(id) : load(id)) : asyncLoad(id);
        public Task<IReadOnlyList<BggBaseGameSearchResult>> SearchAsync(string query, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => throw new NotSupportedException();
    }
}
