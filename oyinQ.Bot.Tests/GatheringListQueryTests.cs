using oyinQ.Bot.Data.Entities;
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
    public void History_ContainsOnlyCompletedAndCancelledAndSortsNewestFirst()
    {
        var values = Apply(GatheringListView.History, GatheringHistoryFilter.All);

        Assert.Equal([6L, 4L, 3L], values.Select(x => x.Id));
        Assert.DoesNotContain(values, x => x.Status == GatheringStatus.Ready);
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
