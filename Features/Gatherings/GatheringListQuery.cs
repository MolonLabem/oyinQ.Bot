using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Data;
using Microsoft.EntityFrameworkCore;

namespace oyinQ.Bot.Features.Gatherings;

public enum GatheringListScope
{
    Upcoming,
    History,
    Completed,
    Cancelled
}

public static class GatheringListQuery
{
    public static IQueryable<GameGathering> ForGame(AppDbContext db, string communityKey, long? bggId)
    {
        if (bggId is null) return db.GameGatherings.Where(x => x.CommunityKey == communityKey);
        if (db.Database.IsRelational())
        {
            var id = bggId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return db.GameGatherings.FromSqlInterpolated($"""
                SELECT * FROM "GameGatherings" WHERE "CommunityKey" = {communityKey}
                AND COALESCE("GameSnapshotJson"->>'bggId', "GameSnapshotJson"->>'BggId') = {id}
                """);
        }
        var ids = db.GameGatherings.AsNoTracking().Where(x => x.CommunityKey == communityKey).AsEnumerable()
            .Where(x => GatheringGameSnapshotSerializer.Deserialize(x.GameSnapshotJson).BggId == bggId).Select(x => x.Id).ToArray();
        return db.GameGatherings.Where(x => x.CommunityKey == communityKey && ids.Contains(x.Id));
    }
    public static bool TryParse(string? scope, string? legacyView, string? legacyStatus,
        out GatheringListScope parsedScope)
    {
        if (!string.IsNullOrWhiteSpace(scope))
        {
            parsedScope = scope.Trim().ToLowerInvariant() switch
            {
                "upcoming" => GatheringListScope.Upcoming,
                "history" => GatheringListScope.History,
                "completed" => GatheringListScope.Completed,
                "cancelled" => GatheringListScope.Cancelled,
                _ => (GatheringListScope)(-1)
            };
            if (!Enum.IsDefined(parsedScope)) return false;
            if (string.IsNullOrWhiteSpace(legacyView) && string.IsNullOrWhiteSpace(legacyStatus)) return true;
            return TryParseLegacy(legacyView, legacyStatus, out var legacyScope)
                && legacyScope == parsedScope;
        }

        return TryParseLegacy(legacyView, legacyStatus, out parsedScope);
    }

    private static bool TryParseLegacy(string? view, string? status, out GatheringListScope parsedScope)
    {
        parsedScope = GatheringListScope.Upcoming;
        if (string.IsNullOrWhiteSpace(view)
            || string.Equals(view, "upcoming", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(status);
        }

        if (!string.Equals(view, "history", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(status))
        {
            parsedScope = GatheringListScope.History;
            return true;
        }
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            parsedScope = GatheringListScope.Completed;
            return true;
        }
        if (string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            parsedScope = GatheringListScope.Cancelled;
            return true;
        }
        return false;
    }

    public static IQueryable<GameGathering> Apply(
        IQueryable<GameGathering> query,
        GatheringListScope scope,
        DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        if (scope == GatheringListScope.Upcoming)
        {
            return query.Where(x => x.StartsAtUtc > now
                    && GatheringLifecycle.ScheduledStatuses.Contains(x.Status))
                .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id);
        }

        query = scope switch
        {
            GatheringListScope.Completed => query.Where(x => x.Status == GatheringStatus.Completed),
            GatheringListScope.Cancelled => query.Where(x => x.Status == GatheringStatus.Cancelled),
            _ => query.Where(x => x.Status == GatheringStatus.Completed
                || x.Status == GatheringStatus.Cancelled
                || (x.StartsAtUtc <= now
                    && GatheringLifecycle.ScheduledStatuses.Contains(x.Status)))
        };
        return query.OrderByDescending(x => x.StartsAtUtc).ThenByDescending(x => x.Id);
    }
}
