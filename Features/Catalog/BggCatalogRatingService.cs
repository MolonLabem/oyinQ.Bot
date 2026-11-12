using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Catalog;

public sealed record CatalogRatings(IReadOnlyDictionary<long, decimal?> Values, bool Pending = false, bool Unavailable = false, bool InitialLoadPending = false)
{
    public static CatalogRatings Empty { get; } = new(new Dictionary<long, decimal?>());
}

public sealed class BggCatalogRatingService(AppDbContext db, IOptions<BggOptions> options, TimeProvider clock)
{
    public async Task<CatalogRatings> ReadAsync(IEnumerable<long> gameIds, CancellationToken ct)
    {
        var ids = gameIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0) return CatalogRatings.Empty;
        var rows = await db.BggRatingCache.AsNoTracking().Where(x => ids.Contains(x.BggId)).ToArrayAsync(ct);
        var now = clock.GetUtcNow();
        if (options.Value.IsAvailable)
        {
            var missing = ids.Except(rows.Select(x => x.BggId)).ToArray();
            if (missing.Length > 0)
            {
                // Concurrent readers may request the same public metadata. Enqueue each ID once.
                await db.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO "BggRatingCache" ("BggId", "RefreshAfter", "Failed", "RefreshRequested")
                    SELECT id, {{now}}, false, true FROM unnest({{missing}}) AS id
                    ON CONFLICT ("BggId") DO NOTHING
                    """, ct);
            }
            await db.BggRatingCache.Where(x => ids.Contains(x.BggId) && !x.RefreshRequested && x.RefreshAfter <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RefreshRequested, true), ct);
            rows = await db.BggRatingCache.AsNoTracking().Where(x => ids.Contains(x.BggId)).ToArrayAsync(ct);
        }
        return new(rows.ToDictionary(x => x.BggId, x => x.Rating),
            options.Value.IsAvailable && rows.Any(x => x.RefreshRequested),
            !options.Value.IsAvailable && ids.Any(id => !rows.Any(x => x.BggId == id && x.CheckedAt.HasValue && !x.Failed)) || rows.Any(x => x.Failed),
            options.Value.IsAvailable && rows.Any(x => x.RefreshRequested && x.CheckedAt is null));
    }
}

public sealed class BggRatingRefreshService(AppDbContext db, IBggRatingSource source, IOptions<BggOptions> options, TimeProvider clock)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        if (!options.Value.IsAvailable) return false;
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        var lease = Guid.NewGuid();
        long[] ids;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var batch = await db.BggRatingCache.FromSqlInterpolated($$"""
                SELECT * FROM "BggRatingCache"
                WHERE "RefreshRequested" AND "RefreshAfter" <= {{now}} AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {{now}})
                ORDER BY "RefreshAfter", "BggId" FOR UPDATE SKIP LOCKED LIMIT 20
                """).ToArrayAsync(ct);
            if (batch.Length == 0) return false;
            ids = batch.Select(x => x.BggId).ToArray();
            foreach (var row in batch) { row.LeaseId = lease; row.LeaseExpiresAt = now.AddMinutes(5); }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        IReadOnlyDictionary<long, decimal?>? values = null;
        try { values = await source.GetRatingsAsync(ids, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { /* Preserve cached ratings and retry later; provider errors cannot break browsing. */ }
        db.ChangeTracker.Clear();
        // Match the lease in the UPDATE itself so a late attempt cannot overwrite a replacement.
        foreach (var id in ids)
        {
            var success = values is not null && values.TryGetValue(id, out var rating) && (rating is null or >= 1 and <= 10);
            var query = db.BggRatingCache.Where(x => x.BggId == id && x.LeaseId == lease);
            var completedAt = clock.GetUtcNow();
            if (success)
                await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.Rating, values![id])
                    .SetProperty(x => x.CheckedAt, completedAt).SetProperty(x => x.RefreshAfter, completedAt.AddDays(1))
                    .SetProperty(x => x.Failed, false).SetProperty(x => x.RefreshRequested, false).SetProperty(x => x.LeaseId, (Guid?)null)
                    .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), ct);
            else
                await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.CheckedAt, completedAt)
                    .SetProperty(x => x.RefreshAfter, completedAt.AddMinutes(15)).SetProperty(x => x.Failed, true).SetProperty(x => x.RefreshRequested, false)
                    .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), ct);
        }
        return true;
    }
}

public sealed class BggRatingRefreshWorker(IServiceScopeFactory scopeFactory, ILogger<BggRatingRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<BggRatingRefreshService>().ProcessOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "BGG rating refresh failed."); }
        }
    }
}
