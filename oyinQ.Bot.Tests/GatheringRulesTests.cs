using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class GatheringRulesTests
{
    [Fact]
    public void Create_StoresBoundedPresentationAndUtcTime()
    {
        var gathering = GatheringRules.Create(
            " Club ",
            new GatheringGameSnapshot(1, 10, "Игра", null, null, 2, 5, "4", []),
            organizerParticipantId: 20,
            new DateTimeOffset(2026, 9, 5, 19, 0, 0, TimeSpan.FromHours(5)),
            minimumPlayers: 3,
            desiredPlayers: 4,
            maximumPlayers: 5,
            "  Новичкам тоже можно. Партия будет плотная.  ",
            canTeachRules: true,
            new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("club", gathering.CommunityKey);
        Assert.Equal("Новичкам тоже можно. Партия будет плотная.", gathering.Description);
        Assert.True(gathering.CanTeachRules);
        Assert.Equal(TimeSpan.Zero, gathering.StartsAtUtc.Offset);
        Assert.Equal(GatheringStatus.Recruiting, gathering.Status);
    }

    [Theory]
    [InlineData(0, 4, 5)]
    [InlineData(4, 3, 5)]
    [InlineData(3, 6, 5)]
    public void ValidatePlayerLimits_RejectsInvalidValues(int minimum, int desired, int maximum) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            GatheringRules.ValidatePlayerLimits(minimum, desired, maximum));

    [Fact]
    public void NormalizeDescription_RejectsMoreThanThreeHundredCharacters() =>
        Assert.Throws<ArgumentException>(() =>
            GatheringRules.NormalizeDescription(new string('я', GatheringRules.DescriptionMaxLength + 1)));

    [Fact]
    public void Create_RejectsStartInPast()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

        var exception = Assert.Throws<InvalidOperationException>(() => GatheringRules.Create(
            "club", new GatheringGameSnapshot(1, 10, "Игра", null, null, 2, 5, "4", []), 20,
            now.AddMinutes(-1), 2, 4, 5, null, true, now));

        Assert.Equal("Дата и время сбора должны быть в будущем.", exception.Message);
    }

    [Fact]
    public void Update_RejectsNewStartInPast()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var gathering = GatheringRules.Create(
            "club", new GatheringGameSnapshot(1, 10, "Игра", null, null, 2, 5, "4", []), 20,
            now.AddHours(2), 2, 4, 5, null, true, now);

        var exception = Assert.Throws<InvalidOperationException>(() => GatheringRules.Update(
            gathering, now.AddMinutes(-1), 2, 4, 5, null, true, [], now));

        Assert.Equal("Дата и время сбора должны быть в будущем.", exception.Message);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("cancel")]
    public void OrganizerLifecycleMutation_RejectsAtScheduledStart(string action)
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var gathering = new GameGathering { StartsAtUtc = now, Status = GatheringStatus.Ready };

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (action == "close") GatheringRules.Close(gathering, now);
            else GatheringRules.Cancel(gathering, null, now);
        });
    }

    [Fact]
    public void ParticipationPolicies_RejectJoiningLeavingAndRejoiningAtScheduledStart()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var gathering = new GameGathering { StartsAtUtc = now, Status = GatheringStatus.Ready };

        Assert.False(GatheringAccessPolicy.CanJoin(gathering, false, false, now));
        Assert.False(GatheringAccessPolicy.CanJoin(gathering, false, false, now.AddMinutes(1)));
        Assert.False(GatheringAccessPolicy.CanLeave(gathering, false, true, now));
    }

    [Fact]
    public void OrganizerActions_AreCanonicalBackendDecisions()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var gathering = new GameGathering
        { StartsAtUtc = now.AddHours(1), Status = GatheringStatus.Ready };

        Assert.True(GatheringAccessPolicy.CanClose(gathering, true, now));
        Assert.False(GatheringAccessPolicy.CanReopen(gathering, true, now));
        Assert.True(GatheringAccessPolicy.CanCancel(gathering, true, now));

        gathering.Status = GatheringStatus.Closed;
        Assert.False(GatheringAccessPolicy.CanClose(gathering, true, now));
        Assert.True(GatheringAccessPolicy.CanReopen(gathering, true, now));

        gathering.Status = GatheringStatus.Completed;
        Assert.False(GatheringAccessPolicy.CanClose(gathering, true, now));
        Assert.False(GatheringAccessPolicy.CanReopen(gathering, true, now));
        Assert.False(GatheringAccessPolicy.CanCancel(gathering, true, now));
    }

    [Fact]
    public void IncreasingCapacity_PromotesWaitlistInOrder()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var gathering = GatheringRules.Create("club",
            new GatheringGameSnapshot(1, 10, "Игра", null, null, 1, 4, null, []), 1,
            now.AddDays(1), 1, 2, 2, null, true, now);
        gathering.Participants.Add(new GameGatheringParticipant
        {
            Id = 10, Status = GatheringParticipationStatus.Confirmed, JoinedAt = now,
            Participant = new Participant { TelegramUserId = 10, DisplayName = "Подтверждён" }
        });
        var first = new GameGatheringParticipant
        {
            Id = 11, Status = GatheringParticipationStatus.Waitlisted, JoinedAt = now.AddMinutes(1),
            Participant = new Participant { TelegramUserId = 11, DisplayName = "Первый" }
        };
        var second = new GameGatheringParticipant
        {
            Id = 12, Status = GatheringParticipationStatus.Waitlisted, JoinedAt = now.AddMinutes(2),
            Participant = new Participant { TelegramUserId = 12, DisplayName = "Второй" }
        };
        gathering.Participants.Add(first);
        gathering.Participants.Add(second);

        var promoted = GatheringRules.Update(gathering, gathering.StartsAtUtc, 1, 3, 4,
            null, true, [], now);

        Assert.Equal([first, second], promoted);
        Assert.All(promoted, x => Assert.Equal(GatheringParticipationStatus.Confirmed, x.Status));
        Assert.Equal(GatheringStatus.Full, gathering.Status);
    }
}
