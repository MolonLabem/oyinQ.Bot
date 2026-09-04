using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class GatheringLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(GatheringStatus.Recruiting, true)]
    [InlineData(GatheringStatus.Ready, true)]
    [InlineData(GatheringStatus.Full, true)]
    [InlineData(GatheringStatus.Closed, true)]
    [InlineData(GatheringStatus.Completed, false)]
    [InlineData(GatheringStatus.Cancelled, false)]
    public void ScheduledStatusSet_IsCanonical(GatheringStatus status, bool expected) =>
        Assert.Equal(expected, GatheringLifecycle.IsScheduled(status));

    [Fact]
    public void EveryPersistedStatus_IsExactlyScheduledOrTerminal()
    {
        foreach (var status in Enum.GetValues<GatheringStatus>())
            Assert.NotEqual(GatheringLifecycle.IsScheduled(status), GatheringLifecycle.IsTerminal(status));
    }

    [Fact]
    public void OrganizerManagement_UsesCanonicalUpcomingLifecycle()
    {
        var gathering = Due(GatheringStatus.Ready, 1);
        gathering.StartsAtUtc = Now.AddMinutes(1);

        Assert.True(GatheringAccessPolicy.CanManage(gathering, true, Now));
        gathering.Status = GatheringStatus.Completed;
        Assert.False(GatheringAccessPolicy.CanManage(gathering, true, Now));
        gathering.Status = GatheringStatus.Ready;
        gathering.StartsAtUtc = Now;
        Assert.False(GatheringAccessPolicy.CanManage(gathering, true, Now));
    }

    [Theory]
    [InlineData(GatheringStatus.Recruiting)]
    [InlineData(GatheringStatus.Closed)]
    public void DueGatheringBelowMinimum_IsRetainedAsCancelled(GatheringStatus status)
    {
        var gathering = Due(status, minimumPlayers: 2);

        var outcome = GatheringLifecycle.ApplyDue(gathering, Now);

        Assert.Equal(GatheringLifecycleOutcome.Cancelled, outcome);
        Assert.Equal(GatheringStatus.Cancelled, gathering.Status);
        Assert.Equal(GatheringLifecycle.InsufficientParticipantsReason, gathering.CancellationReason);
        Assert.Equal(Now, gathering.CancelledAt);
        Assert.Null(gathering.CompletedAt);
        Assert.Equal(GatheringPublicationStatus.Pending, gathering.PublicationStatus);
        Assert.Single(GatheringListQuery.Apply(new[] { gathering }.AsQueryable(), GatheringListScope.Cancelled, Now));
        Assert.Equal(GatheringLifecycleOutcome.None, GatheringLifecycle.ApplyDue(gathering, Now.AddMinutes(1)));
        Assert.Equal(Now, gathering.CancelledAt);
    }

    [Theory]
    [InlineData(GatheringStatus.Ready)]
    [InlineData(GatheringStatus.Full)]
    [InlineData(GatheringStatus.Closed)]
    public void DueGatheringMeetingMinimum_BecomesCompleted(GatheringStatus status)
    {
        var gathering = Due(status, minimumPlayers: 2);
        gathering.Participants.Add(new GameGatheringParticipant
        {
            ParticipantId = 2,
            Status = GatheringParticipationStatus.Confirmed
        });

        var outcome = GatheringLifecycle.ApplyDue(gathering, Now);

        Assert.Equal(GatheringLifecycleOutcome.Completed, outcome);
        Assert.Equal(GatheringStatus.Completed, gathering.Status);
        Assert.Equal(Now, gathering.CompletedAt);
    }

    [Fact]
    public void ManualGuest_CountsTowardMinimumAtLifecycleBoundary()
    {
        var gathering = Due(GatheringStatus.Ready, minimumPlayers: 2);
        gathering.Guests.Add(new GameGatheringGuest { DisplayName = "Гость" });

        Assert.Equal(GatheringLifecycleOutcome.Completed, GatheringLifecycle.ApplyDue(gathering, Now));
        Assert.Equal(GatheringStatus.Completed, gathering.Status);
    }

    [Fact]
    public void DuplicateLifecycleProcessing_IsIdempotentAfterCompletion()
    {
        var gathering = Due(GatheringStatus.Ready, minimumPlayers: 1);

        Assert.Equal(GatheringLifecycleOutcome.Completed, GatheringLifecycle.ApplyDue(gathering, Now));
        Assert.Equal(GatheringLifecycleOutcome.None, GatheringLifecycle.ApplyDue(gathering, Now));
        Assert.Equal(Now, gathering.CompletedAt);
    }

    [Fact]
    public void ChildRows_AreCascadeDeletedByModel()
    {
        using var dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=oyinq_model_test;Username=test;Password=test").Options);

        Assert.Equal(DeleteBehavior.Cascade, dbContext.Model.FindEntityType(typeof(GameGatheringParticipant))!
            .GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(GameGathering)).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, dbContext.Model.FindEntityType(typeof(GameGatheringExpansion))!
            .GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(GameGathering)).DeleteBehavior);
    }

    [Fact]
    public void ManualCancellation_IsRetainedAsCancelled()
    {
        var gathering = Due(GatheringStatus.Ready, 1);
        gathering.StartsAtUtc = Now.AddMinutes(1);

        GatheringRules.Cancel(gathering, "Организатор отменил", Now);

        Assert.Equal(GatheringStatus.Cancelled, gathering.Status);
        Assert.Equal(Now, gathering.CancelledAt);
    }

    private static GameGathering Due(GatheringStatus status, int minimumPlayers) => new()
    {
        PublicId = Guid.NewGuid(),
        StartsAtUtc = Now,
        MinimumPlayers = minimumPlayers,
        DesiredPlayers = minimumPlayers,
        MaximumPlayers = minimumPlayers + 1,
        Status = status,
        Participants = []
    };
}
