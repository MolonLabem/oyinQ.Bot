using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;
public sealed class PlayOutcomeAndReferenceTests
{
    [Fact]
    public async Task DidNotHappen_IsOnlyAnOutcome_WithNoPlayStatisticsOrLinks()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(-2));
        g.Status = GatheringStatus.Completed; await f.Db.SaveChangesAsync();
        var result = await new GatheringPlayService(f.Db, f.Clock).SaveAsync(g.PublicId, "club", f.Me.Id,
            new(false, null, null, [], [], 0), default);
        Assert.Null(result); Assert.Empty(f.Db.GatheringPlayRecords); Assert.False(g.ConfirmedWasPlayed);
        Assert.Equal(1, g.OutcomeRevision); Assert.Equal(GatheringStatus.Completed, g.Status);
        Assert.Single(f.Db.GameGatherings);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new ExternalPlayReferenceService(f.Db, f.Clock)
            .AddAsync(g.PublicId, "club", f.Me.Id, "https://app.bgstatsapp.com/play/1", default));
        Assert.Empty(f.Db.GatheringExternalPlayReferences);
    }

    [Fact]
    public async Task ActualRosterControlsLinks_AuthorsAndOrganizerCanRemove_AndDuplicatesConflict()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(-2));
        g.Status = GatheringStatus.Completed;
        g.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Confirmed });
        var excluded = new Participant { Id = 3, TelegramUserId = 555, DisplayName = "Не играл" };
        g.Participants.Add(new() { Participant = excluded, Status = GatheringParticipationStatus.Confirmed });
        await f.Db.SaveChangesAsync();
        var play = await new GatheringPlayService(f.Db, f.Clock).SaveAsync(g.PublicId, "club", f.Me.Id,
            new(true, f.Clock.Now, null, [f.Me.PublicId, f.Other.PublicId], [], 0), default);
        Assert.True(play!.WasPlayed);
        var service = new ExternalPlayReferenceService(f.Db, f.Clock);
        await service.AddAsync(g.PublicId, "club", f.Other.Id, " https://APP.BGSTATSAPP.COM/play/1 ", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(g.PublicId, "club", f.Me.Id, "https://app.bgstatsapp.com/play/1", default));
        await service.AddAsync(g.PublicId, "club", f.Me.Id, "https://app.bgstatsapp.com/play/2", default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AddAsync(g.PublicId, "club", excluded.Id, "https://app.bgstatsapp.com/play/3", default));
        var mine = await f.Db.GatheringExternalPlayReferences.SingleAsync(x => x.AddedByParticipantId == f.Me.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RemoveAsync(g.PublicId, "club", f.Other.Id, mine.Id, default));
        var other = await f.Db.GatheringExternalPlayReferences.SingleAsync(x => x.AddedByParticipantId == f.Other.Id);
        await service.RemoveAsync(g.PublicId, "club", f.Other.Id, other.Id, default);
        await service.AddAsync(g.PublicId, "club", f.Other.Id, "https://app.bgstatsapp.com/play/3", default);
        other = await f.Db.GatheringExternalPlayReferences.SingleAsync(x => x.AddedByParticipantId == f.Other.Id);
        await service.RemoveAsync(g.PublicId, "club", f.Me.Id, other.Id, default);
        Assert.Single(f.Db.GatheringExternalPlayReferences);
        Assert.Equal(2, play.Players.Count);
    }

    [Fact]
    public void ConfirmationSuggestionsRespectExplicitAttendance_WithoutChangingIt()
    {
        var organizer = new Participant { Id = 1 }; var absent = new Participant { Id = 2 };
        var g = new GameGathering { OrganizerParticipant = organizer };
        g.Participants.Add(new() { Participant = absent, ParticipantId = 2, Status = GatheringParticipationStatus.Confirmed, AttendanceOutcome = AttendanceOutcome.NoShow });
        Assert.Contains(absent.PublicId, GatheringPlayService.PlayerChoices(g).Select(x => x.Id));
        Assert.DoesNotContain(absent.PublicId, GatheringPlayService.SuggestedPlayerIds(g));
        Assert.Equal(AttendanceOutcome.NoShow, g.Participants.Single().AttendanceOutcome);
    }

    [Theory]
    [InlineData("https://bgstats.example/play")]
    [InlineData("https://app.bgstatsapp.com.evil.test/play")]
    [InlineData("https://evil.test/?bgstats=1")]
    [InlineData("https://app.bgstatsapp.com:8443/play")]
    [InlineData("bgstats://app.bgstatsapp.com/play")]
    [InlineData("https://user@app.bgstatsapp.com/play")]
    [InlineData("data:text/html,bgstats")]
    public void Links_RequireExactOfficialHttpsHost(string url) => Assert.Throws<ArgumentException>(() => ExternalPlayReferenceService.Normalize(url));

    [Fact]
    public void FinalRoster_DoesNotGrantWaitlistOrWithdrawnMembership()
    {
        var g = new GameGathering { OrganizerParticipantId = 1 };
        g.Participants.Add(new() { ParticipantId = 2, Status = GatheringParticipationStatus.Waitlisted });
        var play = new GatheringPlayRecord { Gathering = g, WasPlayed = true, Players = [new() { ParticipantId = 3 }] };
        Assert.True(ExternalPlayReferenceService.CanShare(play, 1)); Assert.True(ExternalPlayReferenceService.CanShare(play, 3));
        Assert.False(ExternalPlayReferenceService.CanShare(play, 2)); Assert.False(ExternalPlayReferenceService.CanShare(play, 4));
    }
}
