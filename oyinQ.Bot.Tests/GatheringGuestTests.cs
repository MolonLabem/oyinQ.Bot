using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class GatheringGuestTests
{
    [Fact]
    public void GuestLabel_IsTrimmedAndValidated()
    {
        Assert.Equal("+1 от Виктора", GatheringRules.NormalizeGuestDisplayName("  +1 от Виктора  "));
        Assert.Throws<ArgumentException>(() => GatheringRules.NormalizeGuestDisplayName("   "));
        Assert.Throws<ArgumentException>(() => GatheringRules.NormalizeGuestDisplayName(
            new string('я', GatheringRules.GuestDisplayNameMaxLength + 1)));
    }

    [Fact]
    public void Guests_CountTowardMinimumMaximumAndFullStatus()
    {
        var gathering = Gathering(maximum: 4, minimum: 4);
        gathering.Participants.Add(Membership(2, GatheringParticipationStatus.Confirmed));
        gathering.Guests.Add(new GameGatheringGuest { DisplayName = "Гость 1" });
        GatheringCapacity.SynchronizeScheduledStatus(gathering);
        Assert.Equal(3, GatheringCapacity.OccupiedSeats(gathering));
        Assert.Equal(GatheringStatus.Recruiting, gathering.Status);

        gathering.Guests.Add(new GameGatheringGuest { DisplayName = "Гость 2" });
        GatheringCapacity.SynchronizeScheduledStatus(gathering);
        Assert.Equal(4, GatheringCapacity.OccupiedSeats(gathering));
        Assert.Equal(GatheringStatus.Full, gathering.Status);
    }

    [Fact]
    public async Task Organizer_CanAddRenameAndRemoveGuestWithoutFakeParticipant()
    {
        await using var fixture = await Fixture.CreateAsync(maximum: 4);
        var participantRows = await fixture.Db.Participants.CountAsync();

        await fixture.Service.AddGuestAsync(fixture.Gathering.PublicId, "club", 100,
            "  Жена  ", fixture.Now, default);
        var guest = await fixture.Db.GameGatheringGuests.SingleAsync();
        Assert.Equal("Жена", guest.DisplayName);
        Assert.Equal(fixture.Organizer.Id, guest.CreatedByParticipantId);
        Assert.Equal(participantRows, await fixture.Db.Participants.CountAsync());

        await fixture.Service.RenameGuestAsync(fixture.Gathering.PublicId, guest.Id, "club", 100,
            "+1 от Виктора", fixture.Now, default);
        Assert.Equal("+1 от Виктора", (await fixture.Db.GameGatheringGuests.SingleAsync()).DisplayName);

        await fixture.Service.RemoveGuestAsync(fixture.Gathering.PublicId, guest.Id, "club", 100,
            fixture.Now, default);
        Assert.Empty(await fixture.Db.GameGatheringGuests.ToArrayAsync());
        Assert.Equal(1, GatheringCapacity.OccupiedSeats(fixture.Gathering));
    }

    [Fact]
    public async Task NonOrganizer_CannotManageGuests()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.AddGuestAsync(
            fixture.Gathering.PublicId, "club", 200, "Гость", fixture.Now, default));
    }

    [Fact]
    public async Task GuestId_IsScopedToItsGathering()
    {
        await using var fixture = await Fixture.CreateAsync();
        var other = Gathering();
        other.OrganizerParticipantId = fixture.Organizer.Id;
        other.OrganizerParticipant = fixture.Organizer;
        other.CommunityKey = "club";
        other.Guests.Add(new GameGatheringGuest
        {
            DisplayName = "Другой гость", CreatedByParticipantId = fixture.Organizer.Id,
            CreatedAt = fixture.Now, UpdatedAt = fixture.Now
        });
        fixture.Db.GameGatherings.Add(other);
        await fixture.Db.SaveChangesAsync();
        var forgedId = other.Guests.Single().Id;

        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.RenameGuestAsync(
            fixture.Gathering.PublicId, forgedId, "club", 100, "Подмена", fixture.Now, default));
    }

    [Fact]
    public async Task FinalGuestFillsGathering_AndConcurrentJoinPathCanOnlyWaitlist()
    {
        await using var fixture = await Fixture.CreateAsync(maximum: 3, confirmedParticipant: true);

        await fixture.Service.AddGuestAsync(fixture.Gathering.PublicId, "club", 100,
            "Гость", fixture.Now, default);
        Assert.Equal(GatheringStatus.Full, fixture.Gathering.Status);
        await fixture.Service.JoinAsync(fixture.Gathering.PublicId, "club", 300, fixture.Now, default);

        Assert.Equal(3, GatheringCapacity.OccupiedSeats(fixture.Gathering));
        Assert.Equal(GatheringParticipationStatus.Waitlisted,
            fixture.Gathering.Participants.Single(x => x.ParticipantId == fixture.Joiner.Id).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AddGuestAsync(
            fixture.Gathering.PublicId, "club", 100, "Лишний гость", fixture.Now, default));
        Assert.Equal(3, GatheringCapacity.OccupiedSeats(fixture.Gathering));
    }

    [Fact]
    public async Task RemovingGuest_PromotesExactlyOneWaitlistedParticipant()
    {
        await using var fixture = await Fixture.CreateAsync(maximum: 2);
        await fixture.Service.AddGuestAsync(fixture.Gathering.PublicId, "club", 100,
            "Гость", fixture.Now, default);
        await fixture.Service.JoinAsync(fixture.Gathering.PublicId, "club", 300, fixture.Now, default);
        var guestId = fixture.Gathering.Guests.Single().Id;

        var result = await fixture.Service.RemoveGuestAsync(fixture.Gathering.PublicId, guestId,
            "club", 100, fixture.Now, default);

        Assert.NotNull(result.Promotion);
        Assert.Equal(GatheringParticipationStatus.Confirmed,
            fixture.Gathering.Participants.Single(x => x.ParticipantId == fixture.Joiner.Id).Status);
        Assert.Equal(2, GatheringCapacity.OccupiedSeats(fixture.Gathering));
    }

    [Fact]
    public void CancellationAndHistory_RetainGuests()
    {
        var gathering = Gathering();
        gathering.Guests.Add(new GameGatheringGuest { Id = 7, DisplayName = "Гость" });
        GatheringRules.Cancel(gathering, null, DateTimeOffset.UtcNow);
        Assert.Equal("Гость", Assert.Single(gathering.Guests).DisplayName);
    }

    [Fact]
    public void TelegramAnnouncement_IncludesEscapedGuestAndCanonicalCapacity()
    {
        var gathering = Gathering(maximum: 4);
        gathering.OrganizerParticipant = new Participant { TelegramUserId = 100, DisplayName = "Организатор" };
        gathering.Guests.Add(new GameGatheringGuest { DisplayName = "<Гость>" });

        var announcement = new GatheringPresentationService().BuildTelegramAnnouncement(gathering,
            new BotCommunity("club", "Клуб", -1001, BotMode.Club, "UTC"));

        Assert.Contains("👥 2 /", announcement.HtmlText);
        Assert.Contains("&lt;Гость&gt;", announcement.HtmlText);
        Assert.Contains("(гость)", announcement.HtmlText);
        Assert.DoesNotContain("tg://user", announcement.HtmlText.Split("&lt;Гость&gt;")[1]);
    }

    private static GameGathering Gathering(int maximum = 5, int minimum = 2) => new()
    {
        PublicId = Guid.NewGuid(), CommunityKey = "club", StartsAtUtc = DateTimeOffset.UtcNow.AddDays(2),
        MinimumPlayers = minimum, DesiredPlayers = maximum, MaximumPlayers = maximum,
        Status = GatheringStatus.Recruiting,
        GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new GatheringGameSnapshot(
            GatheringGameSnapshot.CurrentVersion, 1, "Игра", null, null, 1, maximum, null, [], "catalog", [])),
        Participants = [], Guests = []
    };

    private static GameGatheringParticipant Membership(long participantId, GatheringParticipationStatus status) =>
        new() { ParticipantId = participantId, Status = status, JoinedAt = DateTimeOffset.UtcNow };

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db, GatheringService service, GameGathering gathering,
            Participant organizer, Participant joiner, DateTimeOffset now)
        { Db = db; Service = service; Gathering = gathering; Organizer = organizer; Joiner = joiner; Now = now; }

        public AppDbContext Db { get; }
        public GatheringService Service { get; }
        public GameGathering Gathering { get; }
        public Participant Organizer { get; }
        public Participant Joiner { get; }
        public DateTimeOffset Now { get; }

        public static async Task<Fixture> CreateAsync(int maximum = 5, bool confirmedParticipant = false)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
            var organizer = new Participant { TelegramUserId = 100, DisplayName = "Организатор" };
            var member = new Participant { TelegramUserId = 200, DisplayName = "Участник" };
            var joiner = new Participant { TelegramUserId = 300, DisplayName = "Новый участник" };
            var community = new OyinQCommunity
            { Key = "club", Name = "Клуб", Mode = BotMode.Club, TimeZoneId = "UTC", IsActive = true };
            db.AddRange(organizer, member, joiner, community);
            await db.SaveChangesAsync();
            var gathering = Gathering(maximum);
            gathering.OrganizerParticipantId = organizer.Id;
            gathering.OrganizerParticipant = organizer;
            gathering.Community = community;
            if (confirmedParticipant)
                gathering.Participants.Add(new GameGatheringParticipant
                { ParticipantId = member.Id, Participant = member, Status = GatheringParticipationStatus.Confirmed,
                    JoinedAt = DateTimeOffset.UtcNow });
            db.GameGatherings.Add(gathering);
            await db.SaveChangesAsync();
            var service = new GatheringService(db, new CampParticipationPolicy(db, TimeProvider.System));
            return new Fixture(db, service, gathering, organizer, joiner, DateTimeOffset.UtcNow);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
