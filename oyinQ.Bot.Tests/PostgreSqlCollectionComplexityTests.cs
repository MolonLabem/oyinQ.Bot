using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed partial class PostgreSqlStabilizationTests
{
    [PostgreSqlFact]
    public async Task PersonalComplexityBackfillIsScopedIdempotentAndPreservesUnrelatedJson()
    {
        await using var database = await Database.CreateAsync();
        var actor = await SeedAsync(database);
        await using var db = database.Open();
        var other = new Participant { TelegramUserId = 987654, DisplayName = "Other" };
        db.Participants.Add(other); await db.SaveChangesAsync();
        var collection = new ParticipantCollectionService(db);
        await collection.UpsertAsync(actor.Id, [Item(42), Item(43), Item(44), Item(45),
            new(99, CollectionItemType.Expansion, 42, Item(99).Snapshot with { ParentBggIds = [42, 43] })],
            CollectionItemSource.Manual, Now.AddDays(-1), default);
        await collection.UpsertAsync(other.Id, [Item(42)], CollectionItemSource.BggImport, Now.AddDays(-1), default);
        var game = GatheringRules.Create("club", Snapshot(), actor.Id, Now.AddHours(1), 1, 2, 4, null, false, Now);
        db.GameGatherings.Add(game);
        var row = await db.ParticipantCollectionItems.FirstAsync(x => x.BggId == 42);
        var legacy = JsonNode.Parse(row.SnapshotJson)!;
        legacy["version"] = 1; legacy["futureField"] = "preserve";
        row.SnapshotJson = legacy.ToJsonString();
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await db.ParticipantCollectionItems.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var savedGathering = (await db.GameGatherings.AsNoTracking().SingleAsync()).GameSnapshotJson;
        var savedClub = (await db.Clubs.AsNoTracking().SingleAsync()).CollectionJson;
        var calls = new List<string>();
        var client = ComplexityClient(new ComplexityHttp(() => Task.FromResult(
            "<items>" + ComplexityThing(42) + ComplexityThing(43, "0")
            + ComplexityThing(44, expansion: true) + ComplexityThing(99, expansion: true)
            + ComplexityThing(999) + "</items>"), calls));
        var refresh = new ParticipantCollectionComplexityRefresh(db, client, Time);
        var preview = await refresh.RunAsync(false, default);
        Assert.Equal(5, preview.RequestedIds); Assert.Equal(3, preview.PlannedRows);
        Assert.Equal([43L, 44L, 45L], preview.UnresolvedIds); Assert.Equal(0, preview.UpdatedRows);
        Assert.All(await db.ParticipantCollectionItems.AsNoTracking().ToArrayAsync(), x => Assert.Equal(before[x.Id].SnapshotJson, x.SnapshotJson));
        var applied = await refresh.RunAsync(true, default);
        Assert.Equal(3, applied.UpdatedRows); Assert.Equal(0, applied.ConcurrentlyChangedRows);
        var after = await db.ParticipantCollectionItems.AsNoTracking().ToArrayAsync();
        Assert.Equal(before.Count, after.Length);
        foreach (var current in after)
        {
            var original = before[current.Id];
            Assert.Equal(original.Source, current.Source); Assert.Equal(original.ParentBggId, current.ParentBggId);
            Assert.Equal(original.CreatedAt, current.CreatedAt);
            var oldJson = JsonNode.Parse(original.SnapshotJson)!.AsObject();
            var newJson = JsonNode.Parse(current.SnapshotJson)!.AsObject();
            foreach (var key in new[] { "complexity", "complexityWeight" }) { oldJson.Remove(key); newJson.Remove(key); }
            Assert.True(JsonNode.DeepEquals(oldJson, newJson));
            if (current.BggId is 42 or 99)
            {
                Assert.Equal(2.1m, current.ReadSnapshot().ComplexityWeight);
                // Poll plurality wins over the rounded average.
                Assert.Equal(GameComplexity.MediumHeavy, current.ReadSnapshot().Complexity);
            }
            else Assert.Equal(original.SnapshotJson, current.SnapshotJson);
        }
        var replay = await refresh.RunAsync(true, default);
        Assert.Equal(0, replay.PlannedRows); Assert.Equal(0, replay.UpdatedRows);
        Assert.Equal(savedGathering, (await db.GameGatherings.AsNoTracking().SingleAsync()).GameSnapshotJson);
        Assert.Equal(savedClub, (await db.Clubs.AsNoTracking().SingleAsync()).CollectionJson);
        Assert.Empty(await db.CampGameContributions.ToArrayAsync());
        Assert.Contains("id=42,43,44,45,99&", calls[0]);
    }

    [PostgreSqlFact]
    public async Task PersonalComplexityBackfillDoesNotOverwriteConcurrentImportOrRecreateDeletedOwnership()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        await using var db = database.Open();
        await new ParticipantCollectionService(db).UpsertAsync(actor.Id, [Item(42), Item(43), Item(44)], CollectionItemSource.Manual, Now, default);
        var client = ComplexityClient(new ComplexityHttp(async () =>
        {
            await using var editing = database.Open();
            await new ParticipantCollectionService(editing).UpsertAsync(actor.Id,
                [Item(42) with { Snapshot = Item(42).Snapshot with { Name = "Edited during fetch", ComplexityWeight = 4.8m, Complexity = GameComplexity.Heavy } }],
                CollectionItemSource.Manual, Now.AddMinutes(1), default);
            await new ParticipantCollectionService(editing).RemoveAsync(actor.Id, 43, CollectionItemType.BaseGame, default);
            return "<items>" + ComplexityThing(42) + ComplexityThing(43) + ComplexityThing(44) + "</items>";
        }));
        var result = await new ParticipantCollectionComplexityRefresh(db, client, Time).RunAsync(true, default);
        Assert.Equal(1, result.UpdatedRows); Assert.Equal(2, result.ConcurrentlyChangedRows);
        db.ChangeTracker.Clear();
        Assert.False(await db.ParticipantCollectionItems.AnyAsync(x => x.BggId == 43));
        var edited = (await db.ParticipantCollectionItems.SingleAsync(x => x.BggId == 42)).ReadSnapshot();
        Assert.Equal("Edited during fetch", edited.Name); Assert.Equal(4.8m, edited.ComplexityWeight);
        Assert.Equal(GameComplexity.Heavy, edited.Complexity);
    }

    [PostgreSqlFact]
    public async Task PersonalComplexityBackfillProviderFailureWritesNothing()
    {
        await using var database = await Database.CreateAsync(); var actor = await SeedAsync(database);
        await using var db = database.Open();
        await new ParticipantCollectionService(db).UpsertAsync(actor.Id, [Item(42)], CollectionItemSource.Manual, Now, default);
        var saved = (await db.ParticipantCollectionItems.AsNoTracking().SingleAsync()).SnapshotJson;
        var client = ComplexityClient(new ComplexityHttp(() => throw new HttpRequestException("Synthetic BGG failure")));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ParticipantCollectionComplexityRefresh(db, client, Time).RunAsync(true, default));
        Assert.Equal(saved, (await db.ParticipantCollectionItems.AsNoTracking().SingleAsync()).SnapshotJson);
    }

    private static BoardGameGeekClient ComplexityClient(ComplexityHttp handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://boardgamegeek.com") }, RatingOptions);

    private static string ComplexityThing(long id, string weight = "2.1", bool expansion = false) =>
        $"<item type='{(expansion ? "boardgameexpansion" : "boardgame")}' id='{id}'><name type='primary' value='BGG game'/><statistics><ratings><averageweight value='{weight}'/></ratings></statistics>"
        + (weight == "0" ? "" : "<poll name='boardgameweight'><results><result value='4' numvotes='10'/></results></poll>") + "</item>";

    private sealed class ComplexityHttp(Func<Task<string>> response, List<string>? calls = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/xmlapi2/thing", request.RequestUri!.AbsolutePath);
            Assert.Contains("stats=1", request.RequestUri.Query);
            calls?.Add(request.RequestUri.PathAndQuery);
            return new(HttpStatusCode.OK) { Content = new StringContent(await response()) };
        }
    }
}
