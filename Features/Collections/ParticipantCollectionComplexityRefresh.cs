using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public sealed record CollectionComplexityRefreshResult(int RequestedIds, int PlannedRows, int UpdatedRows,
    int ConcurrentlyChangedRows, IReadOnlyList<long> UnresolvedIds);

// Explicit maintenance only: no worker, catalog-read side effect or historical snapshot updates.
public sealed class ParticipantCollectionComplexityRefresh(
    AppDbContext db, IBoardGameGeekClient bgg, TimeProvider timeProvider)
{
    public async Task<CollectionComplexityRefreshResult> RunAsync(bool apply, CancellationToken ct)
    {
        var rows = (await db.ParticipantCollectionItems.AsNoTracking().ToArrayAsync(ct))
            .Where(row => NeedsRefresh(row.ReadSnapshot())).ToArray();
        var ids = rows.Select(row => row.BggId).Distinct().Order().ToArray();
        if (ids.Length == 0) return new(0, 0, 0, 0, []);

        // Reuse the typed, throttled Thing client (20 IDs/request), including its poll parser.
        // Finish all provider I/O before opening a transaction; provider failure writes nothing.
        var providerItems = await bgg.GetItemsByIdsAsync(ids, ct);
        var metadata = providerItems.Where(item => item.Game.BggId is { } id && ids.Contains(id))
            .DistinctBy(item => (item.Game.BggId, item.IsExpansion))
            .ToDictionary(item => (item.Game.BggId!.Value,
                item.IsExpansion ? CollectionItemType.Expansion : CollectionItemType.BaseGame),
                item => BggGameMapper.ToCollectionSnapshot(item.Game, item.ParentBggIds));
        var plans = new List<(ParticipantCollectionItem Row, string Json)>();
        var unresolved = new HashSet<long>();
        foreach (var row in rows)
        {
            var saved = row.ReadSnapshot();
            if (!metadata.TryGetValue((row.BggId, row.ItemType), out var fresh))
            {
                unresolved.Add(row.BggId);
                continue;
            }
            var weight = GameComplexityPresentation.ValidWeight(saved.ComplexityWeight)
                ?? fresh.ComplexityWeight;
            var level = ValidLevel(saved.Complexity) ? saved.Complexity : fresh.Complexity;
            if (weight is null || !ValidLevel(level)) unresolved.Add(row.BggId);
            if (weight == saved.ComplexityWeight && level == saved.Complexity) continue;

            // Keep every unrelated JSON field, including fields unknown to this version.
            var json = JsonNode.Parse(row.SnapshotJson)!.AsObject();
            json["complexityWeight"] = weight;
            json["complexity"] = level is { } value ? (int)value : null;
            plans.Add((row, json.ToJsonString()));
        }
        if (!apply) return new(ids.Length, plans.Count, 0, 0, unresolved.Order().ToArray());

        var updated = 0;
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var (row, json) in plans)
        {
            // Do not recreate removed ownership or overwrite an import/edit made during BGG I/O.
            updated += await db.ParticipantCollectionItems
                .Where(x => x.Id == row.Id && x.SnapshotJson == row.SnapshotJson && x.UpdatedAt == row.UpdatedAt)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.SnapshotJson, json)
                    .SetProperty(x => x.UpdatedAt, now), ct);
        }
        await transaction.CommitAsync(ct);
        return new(ids.Length, plans.Count, updated, plans.Count - updated, unresolved.Order().ToArray());
    }

    private static bool ValidLevel(GameComplexity? level) =>
        level is >= GameComplexity.Light and <= GameComplexity.Heavy;

    private static bool NeedsRefresh(CollectionItemSnapshot snapshot) =>
        GameComplexityPresentation.ValidWeight(snapshot.ComplexityWeight) is null || !ValidLevel(snapshot.Complexity);
}
