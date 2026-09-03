using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class GatheringLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(GatheringStatus.Recruiting)]
    [InlineData(GatheringStatus.Closed)]
    public void DueGatheringBelowMinimum_IsMarkedForHardDeletion(GatheringStatus status)
    {
        var gathering = Due(status, minimumPlayers: 2);

        var outcome = GatheringLifecycle.ApplyDue(gathering, Now);

        Assert.Equal(GatheringLifecycleOutcome.Delete, outcome);
        Assert.NotEqual(GatheringStatus.Cancelled, gathering.Status);
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
    public void FailedGatheringWithTelegramMessage_CreatesCleanupWork()
    {
        var gathering = Due(GatheringStatus.Recruiting, 2);
        gathering.TelegramChatId = -10042;
        gathering.TelegramMessageId = 17;

        var cleanup = GatheringLifecycle.CreateCleanup(gathering, Now);

        Assert.NotNull(cleanup);
        Assert.Equal(-10042, cleanup.TelegramChatId);
        Assert.Equal(17, cleanup.TelegramMessageId);
    }

    [Fact]
    public void FailedGatheringWithoutTelegramMessage_DoesNotCreateCleanupWork() =>
        Assert.Null(GatheringLifecycle.CreateCleanup(Due(GatheringStatus.Recruiting, 2), Now));

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
