using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class ProfileGatheringQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UpcomingSchedule_IsCurrentUserScopedAuthorizedDistinctAndChronological()
    {
        var hosted = Item(1, "club-a", Now.AddHours(3), GatheringStatus.Ready, organizerId: 7);
        hosted.Participants.Add(Member(7, GatheringParticipationStatus.Confirmed));
        hosted.Guests.Add(new GameGatheringGuest { DisplayName = "Гость" });
        var joined = Item(2, "camp", Now.AddHours(1), GatheringStatus.Full, organizerId: 9);
        joined.Participants.Add(Member(7, GatheringParticipationStatus.Confirmed));
        var withdrawn = Item(3, "club-a", Now.AddHours(2), GatheringStatus.Ready, organizerId: 9);
        withdrawn.Participants.Add(Member(7, GatheringParticipationStatus.Withdrawn));
        var cancelled = Item(4, "club-a", Now.AddHours(4), GatheringStatus.Cancelled, organizerId: 7);
        var past = Item(5, "club-a", Now.AddMinutes(-1), GatheringStatus.Ready, organizerId: 7);
        var forbidden = Item(6, "club-b", Now.AddMinutes(30), GatheringStatus.Ready, organizerId: 7);

        var result = ProfileGatheringQuery.Apply(
            new[] { hosted, joined, withdrawn, cancelled, past, forbidden }.AsQueryable(),
            7, ["club-a", "camp"], Now).ToArray();

        Assert.Equal([2L, 1L], result.Select(x => x.Id));
        Assert.Single(result, x => x.Id == 1);
        Assert.DoesNotContain(result, x => x.Id is 3 or 4 or 5 or 6);
    }

    [Fact]
    public void ScheduleAndGatheringDetails_UseIdenticalTimezoneFormatting()
    {
        var gathering = Item(1, "club", new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero),
            GatheringStatus.Ready, 7);
        gathering.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new GatheringGameSnapshot(
            GatheringGameSnapshot.CurrentVersion, 1, "Игра", null, null, 1, 4, null, [], "catalog", []));
        gathering.OrganizerParticipant = new Participant { Id = 7, DisplayName = "Организатор" };
        var service = new GatheringPresentationService();
        var community = new Common.Options.BotCommunity("club", "Клуб", -1001,
            Common.Options.BotMode.Club, "Asia/Qyzylorda");

        Assert.Equal(service.BuildDetails(gathering, community).LocalDateTime,
            service.BuildProfileSchedule(gathering, community, 7).LocalDateTime);
        var schedule = service.BuildProfileSchedule(gathering, community, 7);
        Assert.Equal("2026-09-12", schedule.LocalDate);
        Assert.Equal("19:00", schedule.LocalTime);
        Assert.Equal(gathering.StartsAtUtc, schedule.StartsAtUtc);
    }

    private static GameGathering Item(long id, string community, DateTimeOffset starts,
        GatheringStatus status, long organizerId) => new()
        { Id = id, CommunityKey = community, StartsAtUtc = starts, Status = status,
            OrganizerParticipantId = organizerId, Participants = [], Guests = [] };

    private static GameGatheringParticipant Member(long participantId, GatheringParticipationStatus status) =>
        new() { ParticipantId = participantId, Status = status, JoinedAt = Now };
}
