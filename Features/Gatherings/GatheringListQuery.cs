using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public enum GatheringListView
{
    Upcoming,
    History
}

public enum GatheringHistoryFilter
{
    All,
    Completed,
    Cancelled
}

public static class GatheringListQuery
{
    public static bool TryParse(string? view, string? status,
        out GatheringListView parsedView, out GatheringHistoryFilter parsedFilter)
    {
        parsedView = GatheringListView.Upcoming;
        parsedFilter = GatheringHistoryFilter.All;
        if (string.IsNullOrWhiteSpace(view)
            || string.Equals(view, "upcoming", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(status);
        }
        if (!string.Equals(view, "history", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(status)) return true;
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            parsedFilter = GatheringHistoryFilter.Completed;
            return true;
        }
        if (string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            parsedFilter = GatheringHistoryFilter.Cancelled;
            return true;
        }
        return false;
    }

    public static IQueryable<GameGathering> Apply(
        IQueryable<GameGathering> query,
        GatheringListView view,
        GatheringHistoryFilter filter,
        DateTimeOffset now)
    {
        if (view == GatheringListView.Upcoming)
        {
            return query.Where(x => x.StartsAtUtc > now
                    && GatheringLifecycle.ActiveStatuses.Contains(x.Status))
                .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id);
        }

        query = filter switch
        {
            GatheringHistoryFilter.Completed => query.Where(x => x.Status == GatheringStatus.Completed),
            GatheringHistoryFilter.Cancelled => query.Where(x => x.Status == GatheringStatus.Cancelled),
            _ => query.Where(x => x.Status == GatheringStatus.Completed
                || x.Status == GatheringStatus.Cancelled
                || (x.StartsAtUtc <= now
                    && GatheringLifecycle.ActiveStatuses.Contains(x.Status)))
        };
        return query.OrderByDescending(x => x.StartsAtUtc).ThenByDescending(x => x.Id);
    }
}
