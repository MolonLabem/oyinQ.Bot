using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task CatalogSortsByRatingOrQualifiedGatheringsWithoutInventingPlayedRecords()
    {
        await using var database = await Database.CreateAsync();
        var actor = await SeedAsync(database);
        await using var db = database.Open();
        var club = await db.Clubs.SingleAsync();
        club.CollectionJson = ClubCollectionSerializer.Serialize(new(2, [
            new(42, "Альфа", null, null, 1, 4, null, [], MinPlayTimeMinutes: 45, MaxPlayTimeMinutes: 60),
            new(43, "Бета", null, null, 1, 4, null, []), new(44, "Без оценки", null, null, 1, 4, null, [])]));
        foreach (var (id, rating) in new (long, decimal?)[] { (42, 9m), (43, 7m), (44, null) })
            db.BggRatingCache.Add(new() { BggId = id, Rating = rating, CheckedAt = Now, RefreshAfter = Now.AddDays(1) });
        GameGathering Gathering(long id, GatheringStatus status = GatheringStatus.Completed, bool enough = true, bool future = false) => new()
        {
            PublicId = Guid.NewGuid(), CommunityKey = "club", OrganizerParticipantId = actor.Id,
            StartsAtUtc = future ? Now.AddHours(1) : Now.AddHours(-1), MinimumPlayers = 2, DesiredPlayers = 3, MaximumPlayers = 4,
            Status = status, GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(Snapshot() with { BggId = id }),
            Guests = enough ? [new() { DisplayName = "Гость", CreatedByParticipantId = actor.Id, CreatedAt = Now, UpdatedAt = Now }] : []
        };
        db.GameGatherings.Add(Gathering(42));
        db.GameGatherings.AddRange(Gathering(43), Gathering(43, GatheringStatus.Closed), Gathering(43, GatheringStatus.Ready));
        var notPlayed = Gathering(43); notPlayed.ConfirmedWasPlayed = false; db.GameGatherings.Add(notPlayed);
        db.GameGatherings.Add(Gathering(42, GatheringStatus.Ready, future: true));
        for (var i = 0; i < 5; i++) db.GameGatherings.Add(Gathering(42, GatheringStatus.Cancelled));
        var underfilled = Gathering(42, enough: false);
        underfilled.Participants.Add(new() { Participant = new Participant { TelegramUserId = 45678, DisplayName = "В очереди" },
            Status = GatheringParticipationStatus.Waitlisted, JoinedAt = Now });
        db.GameGatherings.Add(underfilled);
        var foreign = Gathering(42); foreign.CommunityKey = "other";
        db.OyinQCommunities.Add(new() { Key = "other", Name = "Другой клуб", Mode = BotMode.Club, TimeZoneId = "UTC", IsActive = true });
        db.GameGatherings.Add(foreign);
        await db.SaveChangesAsync();
        var policy = new CampParticipationPolicy(db, Time);
        var effective = new EffectiveCampCatalogService(db, new CampContributionSelectionService(db, policy, Time));
        var service = new GameCatalogService(db, effective, Time, new BggCatalogRatingService(db, RatingOptions, Time));
        var rated = await service.ListAsync("club", BotMode.Club, actor.TelegramUserId, new(null, null, [], [], null), default);
        Assert.Equal([42L, 43L, 44L], rated.Items.Select(x => x.BggId));
        Assert.Equal(9m, rated.Items[0].BggRating); Assert.Null(rated.Items[2].BggRating);
        Assert.Equal(45, rated.Items[0].MinPlayTimeMinutes); Assert.Equal(60, rated.Items[0].MaxPlayTimeMinutes);
        Assert.Equal([1, 4, 0], rated.Items.Select(x => x.CompletedGatherings));
        Assert.All(rated.Items, x => Assert.Equal(0, x.RecordedPlays));
        Assert.Empty(await db.GatheringPlayRecords.ToArrayAsync());
        var popular = await service.ListAsync("club", BotMode.Club, actor.TelegramUserId, new(null, null, [], [], "popular"), default);
        Assert.Equal([43L, 42L, 44L], popular.Items.Select(x => x.BggId));
        var alphabetical = await service.ListAsync("club", BotMode.Club, actor.TelegramUserId, new(null, null, [], [], "name"), default);
        Assert.Equal([42L, 44L, 43L], alphabetical.Items.Select(x => x.BggId));
        Assert.Equal(9m, (await service.DetailsAsync("club", BotMode.Club, actor.TelegramUserId, 42, default)).BggRating);
        Assert.Equal(3, (await service.ListAsync("club", BotMode.Club, actor.TelegramUserId, new(null, null, [], [], "rating"), default, countOnly: true)).Total);
    }

    private static readonly Microsoft.Extensions.Options.IOptions<BggOptions> RatingOptions = Options.Create(new BggOptions { ApiToken = "test" });
    private sealed class RatingClock : TimeProvider
    {
        public DateTimeOffset NowValue = Now;
        public override DateTimeOffset GetUtcNow() => NowValue;
    }
    private sealed class RatingSource(Func<IReadOnlyCollection<long>, Task<IReadOnlyDictionary<long, decimal?>>> read) : IBggRatingSource
    {
        public Task<IReadOnlyDictionary<long, decimal?>> GetRatingsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => read(ids);
    }

    [PostgreSqlFact]
    public async Task RatingsQueueOnceSurviveProviderFailureAndRefreshOnlyWhenRead()
    {
        await using var database = await Database.CreateAsync();
        var clock = new RatingClock();
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var db = database.Open();
            Assert.True((await new BggCatalogRatingService(db, RatingOptions, clock).ReadAsync([42, 43, 42], default)).Pending);
        }));
        await using var context = database.Open();
        Assert.Equal(2, await context.BggRatingCache.CountAsync());
        var source = new RatingSource(ids => Task.FromResult<IReadOnlyDictionary<long, decimal?>>(new Dictionary<long, decimal?> { [42] = 8.125m, [43] = null }));
        Assert.True(await new BggRatingRefreshService(context, source, RatingOptions, clock).ProcessOneAsync(default));
        var reader = new BggCatalogRatingService(context, RatingOptions, clock);
        var result = await reader.ReadAsync([42, 43], default);
        Assert.False(result.Pending); Assert.False(result.Unavailable);
        Assert.Equal(8.125m, result.Values[42]); Assert.Null(result.Values[43]);
        clock.NowValue = Now.AddDays(2);
        Assert.False(await new BggRatingRefreshService(context, source, RatingOptions, clock).ProcessOneAsync(default));
        Assert.True((await reader.ReadAsync([42], default)).Pending);
        var failing = new RatingSource(_ => throw new HttpRequestException("unavailable"));
        await new BggRatingRefreshService(context, failing, RatingOptions, clock).ProcessOneAsync(default);
        result = await reader.ReadAsync([42], default);
        Assert.False(result.Pending); Assert.True(result.Unavailable); Assert.Equal(8.125m, result.Values[42]);
        Assert.False(await new BggRatingRefreshService(context, source, RatingOptions, clock).ProcessOneAsync(default));
        clock.NowValue = clock.NowValue.AddMinutes(16);
        Assert.True((await reader.ReadAsync([42], default)).Pending);
        await new BggRatingRefreshService(context, source, RatingOptions, clock).ProcessOneAsync(default);
        Assert.False((await reader.ReadAsync([42], default)).Unavailable);
    }

    [PostgreSqlFact]
    public async Task LateRatingAttemptCannotReplaceRecoveredLease()
    {
        await using var database = await Database.CreateAsync();
        var clock = new RatingClock();
        await using var first = database.Open();
        await new BggCatalogRatingService(first, RatingOptions, clock).ReadAsync([42], default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new RatingSource(async _ => { entered.SetResult(); await resume.Task; return new Dictionary<long, decimal?> { [42] = 2 }; });
        var oldAttempt = new BggRatingRefreshService(first, slow, RatingOptions, clock).ProcessOneAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var second = database.Open();
            var fresh = new RatingSource(_ => Task.FromResult<IReadOnlyDictionary<long, decimal?>>(new Dictionary<long, decimal?> { [42] = 9 }));
            Assert.False(await new BggRatingRefreshService(second, fresh, RatingOptions, clock).ProcessOneAsync(default));
            clock.NowValue = Now.AddMinutes(6);
            Assert.True(await new BggRatingRefreshService(second, fresh, RatingOptions, clock).ProcessOneAsync(default));
        }
        finally { resume.TrySetResult(); }
        await oldAttempt;
        Assert.Equal(9m, (await first.BggRatingCache.AsNoTracking().SingleAsync()).Rating);
    }

    [PostgreSqlFact]
    public async Task MissingOrInvalidRatingsRemainRetryableAndDisabledProviderDoesNotQueue()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Open();
        var disabled = new BggCatalogRatingService(db, Options.Create(new BggOptions()), Time);
        Assert.True((await disabled.ReadAsync([42], default)).Unavailable);
        Assert.Empty(await db.BggRatingCache.ToArrayAsync());
        var reader = new BggCatalogRatingService(db, RatingOptions, Time);
        await reader.ReadAsync([42, 43, 44], default);
        await db.BggRatingCache.Where(x => x.BggId == 42).ExecuteUpdateAsync(s => s.SetProperty(x => x.Rating, 8.12m));
        var source = new BoardGameGeekClient(new HttpClient(new IncompleteRatingHandler()) { BaseAddress = new Uri("https://boardgamegeek.com") }, RatingOptions);
        await new BggRatingRefreshService(db, source, RatingOptions, Time).ProcessOneAsync(default);
        var entries = await db.BggRatingCache.AsNoTracking().OrderBy(x => x.BggId).ToArrayAsync();
        Assert.True(entries[0].Failed); Assert.False(entries[1].Failed); Assert.True(entries[2].Failed);
        Assert.Equal(8.12m, entries[0].Rating); Assert.Null(entries[1].Rating); Assert.Null(entries[2].Rating);
        Assert.False((await reader.ReadAsync([42, 43, 44], default)).Pending);
    }

    private sealed class IncompleteRatingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""
                <items><item id="42"/>
                <item id="43"><statistics><ratings><usersrated value="0"/></ratings></statistics></item>
                <item id="44"><statistics><ratings><usersrated value="20"/><average value="invalid"/></ratings></statistics></item></items>
                """) });
    }
}
