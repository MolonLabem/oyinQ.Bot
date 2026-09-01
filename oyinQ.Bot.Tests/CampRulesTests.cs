using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.MiniApp;

namespace oyinQ.Bot.Tests;

public sealed class CampRulesTests
{
    [Fact]
    public void EnsureRegistrationDatesWithinRange_RejectsDateOutsideNewRange()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            CampRules.EnsureRegistrationDatesWithinRange(
                [new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 5)],
                new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 4)));

        Assert.Contains("подтверждённых дней", error.Message);
    }

    [Fact]
    public void EnsureRegistrationDatesWithinRange_AcceptsInclusiveBoundaries()
    {
        CampRules.EnsureRegistrationDatesWithinRange(
            [new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 4)],
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 4));
    }

    [Fact]
    public void RegistrationDisplayNameFallsBackToTelegramBeforeFirstRegistration()
    {
        Assert.Equal("Sardar", CampParticipantPresentation.RegistrationDisplayName(null, null, " Sardar "));
        Assert.Equal("Игровое имя", CampParticipantPresentation.RegistrationDisplayName(null, "Игровое имя", "Sardar"));
        Assert.Equal("Имя на кэмпе", CampParticipantPresentation.RegistrationDisplayName(
            " Имя на кэмпе ", "Глобальное имя", "Sardar"));
    }

    [Fact]
    public void InclusiveDuration_UsesBothBoundaryDates()
    {
        Assert.Equal(1, CampRules.InclusiveDuration(new(2026, 8, 29), new(2026, 8, 29)));
        Assert.Equal(4, CampRules.InclusiveDuration(new(2026, 8, 29), new(2026, 9, 1)));
    }

    [Fact]
    public void SelectedDates_AreDistinctOrderedAndInsideCamp()
    {
        Assert.Equal([new DateOnly(2026, 8, 29), new DateOnly(2026, 8, 31)],
            CampRules.ValidateSelectedDates([new(2026, 8, 31), new(2026, 8, 29)],
                new(2026, 8, 29), new(2026, 8, 31)));
        Assert.Throws<ArgumentException>(() => CampRules.ValidateSelectedDates(
            [new(2026, 8, 29), new(2026, 8, 29)], new(2026, 8, 29), new(2026, 8, 31)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CampRules.ValidateSelectedDates(
            [new(2026, 9, 1)], new(2026, 8, 29), new(2026, 8, 31)));
    }

    [Theory]
    [InlineData(CampStatus.Draft, CampStatus.Active)]
    [InlineData(CampStatus.Draft, CampStatus.Cancelled)]
    [InlineData(CampStatus.Active, CampStatus.Closed)]
    [InlineData(CampStatus.Active, CampStatus.Cancelled)]
    public void Lifecycle_AllowsOnlyForwardTransitions(CampStatus current, CampStatus next) =>
        CampRules.ValidateTransition(current, next);

    [Theory]
    [InlineData(CampStatus.Active, CampStatus.Draft)]
    [InlineData(CampStatus.Closed, CampStatus.Active)]
    [InlineData(CampStatus.Cancelled, CampStatus.Active)]
    [InlineData(CampStatus.Closed, CampStatus.Cancelled)]
    public void Lifecycle_RejectsBackwardTransitions(CampStatus current, CampStatus next) =>
        Assert.Throws<InvalidOperationException>(() => CampRules.ValidateTransition(current, next));

    [Fact]
    public void BaseSnapshot_IsMutableOnlyInDraft()
    {
        CampRules.EnsureBaseSnapshotMutable(CampStatus.Draft);
        Assert.Throws<InvalidOperationException>(() =>
            CampRules.EnsureBaseSnapshotMutable(CampStatus.Active));
    }

    [Fact]
    public void Closing_RejectsFutureActiveGatherings()
    {
        CampRules.EnsureCanClose(0);
        var error = Assert.Throws<InvalidOperationException>(() => CampRules.EnsureCanClose(2));
        Assert.Contains("2 будущих сборов", error.Message);
    }
}
