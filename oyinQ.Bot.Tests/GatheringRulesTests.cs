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
    public void UpdatePresentation_RejectsCompletedGathering()
    {
        var gathering = new GameGathering { Status = GatheringStatus.Completed };

        Assert.Throws<InvalidOperationException>(() =>
            GatheringRules.UpdatePresentation(gathering, "Описание", true, DateTimeOffset.UtcNow));
    }
}
