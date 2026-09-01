using oyinQ.Bot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class GatheringListQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Upcoming_ContainsOnlyFutureActiveStatusesAndSortsNearestFirst()
    {
        var values = Apply(GatheringListView.Upcoming, GatheringHistoryFilter.All);

        Assert.Equal([2L, 1L], values.Select(x => x.Id));
        Assert.All(values, x => Assert.True(x.StartsAtUtc > Now));
        Assert.Contains(values, x => x.Status == GatheringStatus.Closed);
    }

    [Fact]
    public void History_ContainsTerminalAndDueActiveStatusesAndSortsNewestFirst()
    {
        var values = Apply(GatheringListView.History, GatheringHistoryFilter.All);

        Assert.Equal([6L, 5L, 4L, 3L], values.Select(x => x.Id));
    }

    [Fact]
    public void FutureActiveGathering_IsUpcomingAndNotHistory()
    {
        var upcoming = Apply(GatheringListView.Upcoming, GatheringHistoryFilter.All);
        var history = Apply(GatheringListView.History, GatheringHistoryFilter.All);

        Assert.Contains(upcoming, x => x.Id == 1);
        Assert.DoesNotContain(history, x => x.Id == 1);
    }

    [Theory]
    [InlineData(GatheringHistoryFilter.Completed, GatheringStatus.Completed)]
    [InlineData(GatheringHistoryFilter.Cancelled, GatheringStatus.Cancelled)]
    public void HistoryFilter_ReturnsOnlyRequestedStatus(
        GatheringHistoryFilter filter, GatheringStatus expected)
    {
        var values = Apply(GatheringListView.History, filter);

        Assert.NotEmpty(values);
        Assert.All(values, x => Assert.Equal(expected, x.Status));
    }

    [Fact]
    public void CancelledFutureGathering_IsHistoryAndNeverUpcoming()
    {
        var history = Apply(GatheringListView.History, GatheringHistoryFilter.Cancelled);
        var upcoming = Apply(GatheringListView.Upcoming, GatheringHistoryFilter.All);

        Assert.Contains(history, x => x.Id == 6);
        Assert.DoesNotContain(upcoming, x => x.Id == 6);
    }

    [Fact]
    public void PagingAfterFilteringPreservesFilterAndDeterministicOrder()
    {
        var values = Apply(GatheringListView.History, GatheringHistoryFilter.Cancelled)
            .Skip(1).Take(1).ToArray();

        Assert.Equal([4L], values.Select(x => x.Id));
        Assert.All(values, x => Assert.Equal(GatheringStatus.Cancelled, x.Status));
    }

    [Fact]
    public void CancelledHistoryPaging_TranslatesForNpgsql()
    {
        using var dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=oyinq_translation_test;Username=test;Password=test").Options);

        var sql = GatheringListQuery.Apply(dbContext.GameGatherings.AsNoTracking(),
                GatheringListView.History, GatheringHistoryFilter.Cancelled, Now)
            .Skip(20).Take(21).ToQueryString();

        Assert.Contains("WHERE g.\"Status\" = 5", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY g.\"StartsAtUtc\" DESC, g.\"Id\" DESC", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GatheringStatus.Recruiting)]
    [InlineData(GatheringStatus.Ready)]
    [InlineData(GatheringStatus.Full)]
    [InlineData(GatheringStatus.Closed)]
    public void ActiveStatusAtStartBoundary_IsHistoryUntilLifecycleRuns(GatheringStatus status)
    {
        var item = Item(20, status, Now);

        Assert.Empty(GatheringListQuery.Apply(new[] { item }.AsQueryable(), GatheringListView.Upcoming, GatheringHistoryFilter.All, Now));
        Assert.Equal([item], GatheringListQuery.Apply(new[] { item }.AsQueryable(), GatheringListView.History, GatheringHistoryFilter.All, Now));
        Assert.Empty(GatheringListQuery.Apply(new[] { item }.AsQueryable(), GatheringListView.History, GatheringHistoryFilter.Completed, Now));
        Assert.Empty(GatheringListQuery.Apply(new[] { item }.AsQueryable(), GatheringListView.History, GatheringHistoryFilter.Cancelled, Now));
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData("upcoming", "completed")]
    [InlineData("history", "failed")]
    public void UnsupportedViewOrFilter_IsRejected(string view, string? status) =>
        Assert.False(GatheringListQuery.TryParse(view, status, out _, out _));

    private static GameGathering[] Apply(GatheringListView view, GatheringHistoryFilter filter)
    {
        var source = new[]
        {
            Item(1, GatheringStatus.Ready, Now.AddHours(2)),
            Item(2, GatheringStatus.Closed, Now.AddHours(1)),
            Item(3, GatheringStatus.Completed, Now.AddHours(-2)),
            Item(4, GatheringStatus.Cancelled, Now.AddHours(-1)),
            Item(5, GatheringStatus.Recruiting, Now),
            Item(6, GatheringStatus.Cancelled, Now.AddHours(3))
        };
        return GatheringListQuery.Apply(source.AsQueryable(), view, filter, Now).ToArray();
    }

    private static GameGathering Item(long id, GatheringStatus status, DateTimeOffset startsAt) =>
        new() { Id = id, Status = status, StartsAtUtc = startsAt };
}
