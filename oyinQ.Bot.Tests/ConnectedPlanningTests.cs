using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class ConnectedPlanningTests
{
    [Fact]
    public void StoredFiltersRespectPollCategoryMechanicsAndUnknownDuration()
    {
        var game = Game() with { ComplexityWeight = 1.2m, Complexity = GameComplexity.Heavy, MaxPlayTimeMinutes = 90, Mechanics = [new(1, "Mechanic")] };
        var query = new CatalogQuery(null, null, [], [], null, ComplexityLevels: [GameComplexity.Heavy], MaxDurationMinutes: 90, MechanicIds: [1]);
        Assert.True(GameCatalogService.Matches(game, query));
        Assert.False(GameCatalogService.Matches(game, query with { ComplexityLevels = [GameComplexity.Light] }));
        Assert.False(GameCatalogService.Matches(game, query with { MechanicIds = [2] }));
        Assert.False(GameCatalogService.Matches(game, query with { MaxDurationMinutes = 89 }));
        Assert.False(GameCatalogService.Matches(game with { MaxPlayTimeMinutes = null }, query));
    }

    [Fact]
    public async Task AgendaIncludesWaitlistAndStartedButExcludesOtherScopesAndConfirmedHistory()
    {
        await using var f = new PlanningFixture(); var future = f.Gathering("club", f.Clock.Now.AddHours(1));
        future.OrganizerParticipant = f.Other; future.OrganizerParticipantId = f.Other.Id;
        future.Participants.Add(new() { Participant = f.Me, Status = GatheringParticipationStatus.Waitlisted });
        var started = f.Gathering("club", f.Clock.Now.AddMinutes(-30)); started.Status = GatheringStatus.Completed;
        var old = f.Gathering("club", f.Clock.Now.AddDays(-3)); old.Status = GatheringStatus.Completed;
        var played = f.Gathering("club", f.Clock.Now.AddMinutes(-10)); played.Status = GatheringStatus.Completed; played.ConfirmedWasPlayed = true;
        f.Gathering("secret", f.Clock.Now.AddHours(1));
        await f.Db.SaveChangesAsync();
        var rows = await ProfileGatheringQuery.AgendaCandidates(f.Db.GameGatherings.Include(x => x.Participants), f.Me.Id, ["club"], f.Clock.Now).ToArrayAsync();
        var agenda = rows.Where(x => ProfileGatheringQuery.IsInAgenda(x, f.Clock.Now)).ToArray();
        Assert.Equal(new[] { started.PublicId, future.PublicId }, agenda.Select(x => x.PublicId));
        var presentation = new GatheringPresentationService();
        Assert.Equal("started", presentation.BuildProfileSchedule(started, started.Community.ToBotCommunity(), f.Me.Id, f.Clock.Now).Progress);
        Assert.Equal(1, presentation.BuildProfileSchedule(future, future.Community.ToBotCommunity(), f.Me.Id, f.Clock.Now).WaitlistPosition);
    }

    [Fact]
    public async Task LongGameConflictUsesIntervalsRatherThanNearbyStartTimes()
    {
        await using var f = new PlanningFixture(); var longGame = f.Gathering("club", f.Clock.Now.AddHours(1));
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(longGame.GameSnapshotJson) with { MaxPlayTimeMinutes = 300 };
        longGame.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(snapshot); await f.Db.SaveChangesAsync();
        var service = new GatheringScheduleConflictService(f.Db);
        await Assert.ThrowsAsync<GatheringScheduleConflictException>(() => service.WarnAsync(f.Me.Id, longGame.StartsAtUtc.AddHours(4), null, false, f.Clock.Now, default, snapshot));
        await service.WarnAsync(f.Me.Id, longGame.StartsAtUtc.AddHours(5), null, false, f.Clock.Now, default, snapshot);
        Assert.True(GatheringPlayTiming.Overlaps(DateTimeOffset.Parse("2026-09-14T23:30:00Z"), DateTimeOffset.Parse("2026-09-15T01:30:00Z"),
            DateTimeOffset.Parse("2026-09-15T01:00:00Z"), DateTimeOffset.Parse("2026-09-15T02:00:00Z")));
    }

    [Fact]
    public async Task CopyUsesSavedSnapshotWithoutProviderAndStartsAnEmptyRoster()
    {
        await using var f = new PlanningFixture(); var original = f.Gathering("club", f.Clock.Now.AddDays(-1));
        original.Status = GatheringStatus.Completed; original.ConfirmedWasPlayed = true;
        original.Guests.Add(new() { DisplayName = "Гость" });
        original.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Confirmed });
        await f.Db.SaveChangesAsync();
        var service = Management(f);
        var command = new CreateGatheringCommand("club", "catalog", 42, [], f.Clock.Now.AddHours(2), 1, 2, 4, "Повтор", true,
            OperationId: Guid.NewGuid(), CopyFromPublicId: original.PublicId);
        var created = await service.CreateAsync(original.Community.ToBotCommunity(), new(f.Me.TelegramUserId), command, default);
        Assert.NotEqual(original.PublicId, created.PublicId); Assert.Empty(created.Participants); Assert.Empty(created.Guests);
        Assert.Null(created.ConfirmedWasPlayed); Assert.Null(created.TelegramMessageId); Assert.Equal("Повтор", created.Description);
        Assert.Equal(created.PublicId, (await service.CreateAsync(original.Community.ToBotCommunity(), new(f.Me.TelegramUserId), command, default)).PublicId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(original.Community.ToBotCommunity(), new(f.Other.TelegramUserId), command, default));
        Assert.Single(original.Participants); Assert.Single(original.Guests);
    }

    [Fact]
    public async Task DemandIsScopedAndCreatesWithoutEnrollingInterestedUsersOrOwnership()
    {
        await using var f = new PlanningFixture(); var seed = f.Gathering("club", f.Clock.Now.AddDays(-3)); seed.Status = GatheringStatus.Cancelled;
        var json = ClubCollectionSerializer.Serialize(new(2, [Game()]));
        f.Db.GameWishes.Add(new() { CommunityKey = "club", Participant = f.Other, BggId = 42, SnapshotJson = json });
        f.Db.GameWishes.Add(new() { CommunityKey = "other", Participant = f.Me, BggId = 99, SnapshotJson = ClubCollectionSerializer.Serialize(new(2, [Game() with { BggId = 99 }])) });
        await f.Db.SaveChangesAsync();
        var catalog = new GameCatalogService(f.Db, null!, f.Clock);
        var demand = Assert.Single(await catalog.DemandAsync("club", BotMode.Club, f.Me.TelegramUserId, default));
        Assert.Equal(42, demand.Game.BggId); Assert.Equal(1, demand.InterestedParticipants); Assert.Equal(0, demand.ScheduledGatherings);
        Assert.Empty(await catalog.LoadClubAsync("club", f.Me.TelegramUserId, default));
        var created = await Management(f).CreateAsync(seed.Community.ToBotCommunity(), new(f.Me.TelegramUserId),
            new("club", "demand", 42, [], f.Clock.Now.AddHours(1), 1, 2, 4, null, true), default);
        Assert.Empty(created.Participants); Assert.Empty(f.Db.ParticipantCollectionItems); Assert.Empty(f.Db.CampGameContributions);
        Assert.Equal(2, await f.Db.GameWishes.CountAsync());
        Assert.Equal(1, (await catalog.DemandAsync("club", BotMode.Club, f.Me.TelegramUserId, default)).Single().ScheduledGatherings);
    }

    [Fact]
    public void CalendarUsesStableUidUtcAndUtf8FoldingAndEscapesTextInjection()
    {
        var start = DateTimeOffset.Parse("2026-09-14T23:30:00+05:00"); var id = Guid.NewGuid();
        var item = new ProfileGatheringPresentation(id, "club", "Клуб; друзья", BotMode.Club,
            new string('Я', 80) + "🎲\r\nEND:VEVENT", start, "2026-09-14", "23:30", "", false, start.AddHours(2), 2);
        var bytes = GatheringCalendarExport.Build([item], start, new MiniAppLinkBuilder(Options.Create(new BotOptions { PublicBaseUrl = "https://example.com" })));
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.All(text.Split("\r\n"), line => Assert.InRange(Encoding.UTF8.GetByteCount(line), 0, 75));
        var unfolded = text.Replace("\r\n ", "");
        Assert.Contains($"UID:{id:N}@oyinq", unfolded); Assert.Contains("DTSTART:20260914T183000Z", unfolded);
        Assert.Contains("DTEND:20260914T203000Z", unfolded); Assert.Contains("STATUS:TENTATIVE", unfolded);
        Assert.Contains("\\nEND:VEVENT", unfolded); Assert.Equal(1, text.Split("\r\nEND:VEVENT\r\n").Length - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyAndDemandSaveNewProviderExpansionsWithoutCreatingOwnership(bool copy)
    {
        await using var f = new PlanningFixture();
        var original = f.Gathering("club", f.Clock.Now.AddDays(-1));
        original.Status = GatheringStatus.Completed;
        f.Db.GameWishes.Add(new() { CommunityKey = "club", Participant = f.Other, BggId = 42,
            SnapshotJson = ClubCollectionSerializer.Serialize(new(2, [Game()])) });
        await f.Db.SaveChangesAsync();
        var client = new NoBgg(new(new ExternalGame(42, "Игра", 1, 4, null, null),
            [new(99, "Дополнение", MinPlayers: 1, MaxPlayers: 4)]));
        var service = Management(f, client);
        var created = await service.CreateAsync(original.Community.ToBotCommunity(), new(f.Me.TelegramUserId),
            new("club", copy ? "catalog" : "demand", 42, [99], f.Clock.Now.AddHours(2), 1, 2, 4, null, true,
                CopyFromPublicId: copy ? original.PublicId : null), default);
        f.Db.ChangeTracker.Clear();
        var saved = await f.Db.GameGatherings.SingleAsync(x => x.PublicId == created.PublicId);
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(saved.GameSnapshotJson);
        Assert.Equal(99, Assert.Single(snapshot.SelectedExpansions).BggId);
        Assert.Empty(f.Db.ParticipantCollectionItems);
        Assert.Empty(f.Db.CampGameContributions);
        Assert.Single(f.Db.GameWishes);
    }

    private static ClubCollectionGame Game() => new(42, "Игра", null, null, 1, 4, null, []);
    [Fact]
    public async Task CampCatalogAndDetailsUseTheSelectedAttendanceDay()
    {
        await using var f = new PlanningFixture();
        var community = new OyinQCommunity { Key = "camp", Name = "Camp", Mode = BotMode.Camp, TimeZoneId = "UTC", IsActive = true };
        var camp = new Camp { BotChat = community, Status = CampStatus.Active, StartsAtUtc = f.Clock.Now.AddDays(-1), EndsAtUtc = f.Clock.Now.AddDays(2) };
        var day = DateOnly.FromDateTime(f.Clock.Now.UtcDateTime);
        f.Db.Camps.Add(camp);
        f.Db.CampRegistrations.Add(new() { Camp = camp, Participant = f.Other, City = "Алматы", NeedsAccommodation = false, SelectedDays = [new() { Date = day }] });
        f.Db.CampGameContributions.Add(new() { Camp = camp, Participant = f.Other, BggId = 42, ItemType = CollectionItemType.BaseGame, Commitment = CampBringCommitment.Bringing,
            SnapshotJson = CollectionItemSnapshotSerializer.Serialize(new(CollectionItemSnapshot.CurrentVersion, "Игра", null, null, 1, 4, null)) });
        // A wish keeps the game browseable even on the day without a provider; it must not imply availability.
        f.Db.GameWishes.Add(new() { Community = community, Participant = f.Me, BggId = 42, SnapshotJson = ClubCollectionSerializer.Serialize(new(2, [Game()])) });
        await f.Db.SaveChangesAsync();
        var policy = new Features.Communities.CampParticipationPolicy(f.Db, f.Clock);
        var catalog = new GameCatalogService(f.Db, new(f.Db, new(f.Db, policy, f.Clock)), f.Clock);
        var query = new CatalogQuery(null, null, [], [], null, AttendanceDate: day);
        Assert.True((await catalog.ListAsync("camp", BotMode.Camp, f.Me.TelegramUserId, query, default)).Items.Single().IsDefinitelyAvailable);
        Assert.False((await catalog.ListAsync("camp", BotMode.Camp, f.Me.TelegramUserId, query with { AttendanceDate = day.AddDays(1) }, default)).Items.Single().IsDefinitelyAvailable);
        Assert.Single((await catalog.DetailsAsync("camp", BotMode.Camp, f.Me.TelegramUserId, 42, default, day)).Availability.Providers);
        Assert.Empty((await catalog.DetailsAsync("camp", BotMode.Camp, f.Me.TelegramUserId, 42, default, day.AddDays(1))).Availability.Providers);
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.ListAsync("camp", BotMode.Camp, f.Me.TelegramUserId, query with { AttendanceDate = day.AddDays(5) }, default));
    }

    private static GatheringManagementService Management(PlanningFixture f, IBoardGameGeekClient? client = null) => new(f.Db, new(f.Db, client ?? new NoBgg()), new(f.Db, f.Clock), new(f.Db, new(f.Db, f.Clock)), f.Clock);
    private sealed class NoBgg(BggGameDetails? details = null) : IBoardGameGeekClient
    {
        public Task<BggGameDetails?> GetGameDetailsAsync(long id, CancellationToken ct) => details is not null
            ? Task.FromResult<BggGameDetails?>(details) : throw new InvalidOperationException("No provider call expected");
        public Task<IReadOnlyList<BggBaseGameSearchResult>> SearchAsync(string q, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => throw new InvalidOperationException("No provider call expected");
    }
}
