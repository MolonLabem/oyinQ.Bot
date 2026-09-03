using oyinQ.Bot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class GatheringListQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Upcoming_ContainsOnlyFutureScheduledStatusesAndSortsNearestFirst()
    {
        var values = Apply(GatheringListScope.Upcoming);

        Assert.Equal([2L, 1L], values.Select(x => x.Id));
        Assert.All(values, x => Assert.True(x.StartsAtUtc > Now));
        Assert.Contains(values, x => x.Status == GatheringStatus.Closed);
    }

    [Fact]
    public void History_ContainsTerminalAndDueScheduledStatusesAndSortsNewestFirst()
    {
        var values = Apply(GatheringListScope.History);

        Assert.Equal([6L, 5L, 4L, 3L], values.Select(x => x.Id));
    }

    [Fact]
    public void FutureActiveGathering_IsUpcomingAndNotHistory()
    {
        var upcoming = Apply(GatheringListScope.Upcoming);
        var history = Apply(GatheringListScope.History);

        Assert.Contains(upcoming, x => x.Id == 1);
        Assert.DoesNotContain(history, x => x.Id == 1);
    }

    [Theory]
    [InlineData(GatheringListScope.Completed, GatheringStatus.Completed)]
    [InlineData(GatheringListScope.Cancelled, GatheringStatus.Cancelled)]
    public void HistoryFilter_ReturnsOnlyRequestedStatus(
        GatheringListScope scope, GatheringStatus expected)
    {
        var values = Apply(scope);

        Assert.NotEmpty(values);
        Assert.All(values, x => Assert.Equal(expected, x.Status));
    }

    [Fact]
    public void CancelledFutureGathering_IsHistoryAndNeverUpcoming()
    {
        var history = Apply(GatheringListScope.Cancelled);
        var upcoming = Apply(GatheringListScope.Upcoming);

        Assert.Contains(history, x => x.Id == 6);
        Assert.DoesNotContain(upcoming, x => x.Id == 6);
    }

    [Fact]
    public void PagingAfterFilteringPreservesFilterAndDeterministicOrder()
    {
        var values = Apply(GatheringListScope.Cancelled)
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
                GatheringListScope.Cancelled, Now)
            .Skip(20).Take(21).ToQueryString();

        Assert.Contains("WHERE g.\"Status\" = 5", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY g.\"StartsAtUtc\" DESC, g.\"Id\" DESC", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalActiveStatusSet_TranslatesForNpgsql()
    {
        using var dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=oyinq_translation_test;Username=test;Password=test").Options);

        var sql = GatheringListQuery.Apply(dbContext.GameGatherings.AsNoTracking(),
            GatheringListScope.Upcoming, Now).ToQueryString();

        Assert.Contains("Status", sql, StringComparison.Ordinal);
        Assert.Contains("StartsAtUtc", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GatheringStatus.Recruiting)]
    [InlineData(GatheringStatus.Ready)]
    [InlineData(GatheringStatus.Full)]
    [InlineData(GatheringStatus.Closed)]
    public void ActiveStatusAtStartBoundary_IsHistoryUntilLifecycleRuns(GatheringStatus status)
    {
        var item = Item(20, status, Now);

        Assert.Empty(GatheringListQuery.Apply(new[] { item }.AsQueryable(), GatheringListScope.Upcoming, Now));
        Assert.Equal([item], GatheringListQuery.Apply(new[] { item }.AsQueryable(), GatheringListScope.History, Now));
        Assert.Empty(GatheringListQuery.Apply(new[] { item }.AsQueryable(), GatheringListScope.Completed, Now));
        Assert.Empty(GatheringListQuery.Apply(new[] { item }.AsQueryable(), GatheringListScope.Cancelled, Now));
    }

    [Theory]
    [InlineData("unknown", null, null)]
    [InlineData("upcoming", "history", null)]
    [InlineData("completed", "history", "cancelled")]
    [InlineData(null, "upcoming", "completed")]
    [InlineData(null, "history", "failed")]
    public void UnsupportedOrAmbiguousScope_IsRejected(string? scope, string? view, string? status) =>
        Assert.False(GatheringListQuery.TryParse(scope, view, status, out _));

    [Theory]
    [InlineData("upcoming", GatheringListScope.Upcoming)]
    [InlineData("history", GatheringListScope.History)]
    [InlineData("completed", GatheringListScope.Completed)]
    [InlineData("cancelled", GatheringListScope.Cancelled)]
    public void CanonicalScope_ParsesWithoutSecondaryState(string scope, GatheringListScope expected)
    {
        Assert.True(GatheringListQuery.TryParse(scope, null, null, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void LegacyViewAndStatus_RemainCompatibleDuringRollout()
    {
        Assert.True(GatheringListQuery.TryParse(null, "history", "completed", out var parsed));
        Assert.Equal(GatheringListScope.Completed, parsed);
    }

    [Theory]
    [InlineData("upcoming", "upcoming", null, GatheringListScope.Upcoming)]
    [InlineData("history", "history", null, GatheringListScope.History)]
    [InlineData("completed", "history", "completed", GatheringListScope.Completed)]
    [InlineData("cancelled", "history", "cancelled", GatheringListScope.Cancelled)]
    public void EquivalentCanonicalAndLegacyParameters_AreRolloutSafe(string scope, string view,
        string? status, GatheringListScope expected)
    {
        Assert.True(GatheringListQuery.TryParse(scope, view, status, out var parsed));
        Assert.Equal(expected, parsed);
    }

    private static GameGathering[] Apply(GatheringListScope scope)
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
        return GatheringListQuery.Apply(source.AsQueryable(), scope, Now).ToArray();
    }

    private static GameGathering Item(long id, GatheringStatus status, DateTimeOffset startsAt) =>
        new() { Id = id, Status = status, StartsAtUtc = startsAt };
}
