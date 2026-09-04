using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class PlanningFeatureTests
{
    [Theory]
    [InlineData("2026-09-04T08:59", false)]
    [InlineData("2026-09-04T09:00", true)]
    [InlineData("2026-09-06T08:59", true)]
    [InlineData("2026-09-06T09:00", false)]
    [InlineData("2026-09-06T12:00", false)]
    public void Camp_ExactOpeningAndExclusiveClosing(string local, bool valid)
    {
        var camp = Camp();
        var instant = CommunityTime.ParseLocal(local, camp.BotChat.TimeZoneId);
        if (valid) CampOperatingWindow.RequireContains(camp, instant);
        else Assert.Throws<InvalidOperationException>(() => CampOperatingWindow.RequireContains(camp, instant));
        Assert.Equal(new DateOnly(2026, 9, 6), camp.EndDate);
    }

    [Fact]
    public void Camp_PartialDaysAndMidnightAreDerivedInCommunityTimeZone()
    {
        var camp = Camp();
        var labels = CampOperatingWindow.AttendanceLabels(camp);
        Assert.Equal("с 09:00", labels["2026-09-04"]);
        Assert.Equal("до 09:00", labels["2026-09-06"]);
        camp.EndsAtUtc = CommunityTime.ParseLocal("2026-09-06T00:00", camp.BotChat.TimeZoneId);
        Assert.Equal(new DateOnly(2026, 9, 5), camp.EndDate);
        Assert.False(CampOperatingWindow.AttendanceLabels(camp).ContainsKey("2026-09-06"));
        Assert.True(CampParticipationPolicy.HasEnded(camp, "Asia/Almaty", camp.EndsAtUtc.Value));
    }

    [Theory]
    [InlineData("2026-03-29T02:30")]
    [InlineData("2026-10-25T02:30")]
    public void Camp_RejectsAmbiguousAndNonexistentLocalTime(string local) =>
        Assert.Throws<ArgumentException>(() => CommunityTime.ParseLocal(local, "Europe/Berlin"));

    [Fact]
    public void Migration_PreservesWholeOldEndDateAndBackfillsOpaqueIdentifiers()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=test;Username=test;Password=test").Options);
        var script = db.GetService<IMigrator>().GenerateScript("20260904072034_PersistentParticipantCollection", "20260904084352_GatheringPlayRecords");
        Assert.Contains("(c.\"EndDate\" + 1)::timestamp AT TIME ZONE o.\"TimeZoneId\"", script);
        Assert.True(script.IndexOf("UPDATE \"Camps\"", StringComparison.Ordinal) < script.IndexOf("DROP COLUMN \"EndDate\"", StringComparison.Ordinal));
        Assert.Contains("gen_random_uuid()", script);
        Assert.DoesNotContain("INSERT INTO \"Notifications\"", script);
        Assert.DoesNotContain("UPDATE \"GameGatherings\"", script);
    }

    [Fact]
    public async Task Conflicts_AreCrossCommunitySoftWarnings_ExcludeWaitlistsAndSelf()
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("club", f.Clock.Now.AddHours(2));
        var second = f.Gathering("second", g.StartsAtUtc.AddMinutes(90));
        second.OrganizerParticipant = f.Other; second.OrganizerParticipantId = f.Other.Id;
        second.Participants.Add(new() { Participant = f.Me, Status = GatheringParticipationStatus.Waitlisted });
        await f.Db.SaveChangesAsync();
        var service = new GatheringScheduleConflictService(f.Db);
        await service.WarnAsync(f.Me.Id, g.StartsAtUtc, g.PublicId, false, f.Clock.Now, default);
        second.Participants.Single().Status = GatheringParticipationStatus.Confirmed;
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<GatheringScheduleConflictException>(() => service.WarnAsync(f.Me.Id, g.StartsAtUtc, g.PublicId, false, f.Clock.Now, default));
        Assert.Equal("second", Assert.Single(error.Conflicts).CommunityKey);
        await service.WarnAsync(f.Me.Id, g.StartsAtUtc, g.PublicId, true, f.Clock.Now, default);
        second.Status = GatheringStatus.Cancelled; await f.Db.SaveChangesAsync();
        await service.WarnAsync(f.Me.Id, g.StartsAtUtc, g.PublicId, false, f.Clock.Now, default);
    }

    [Fact]
    public async Task ConflictConfirmation_DoesNotBypassClosedJoinOrCampRegistration()
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("club", f.Clock.Now.AddHours(2)); g.Status = GatheringStatus.Closed;
        await f.Db.SaveChangesAsync();
        var service = new GatheringService(f.Db, new CampParticipationPolicy(f.Db, f.Clock));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.JoinAsync(g.PublicId, "club", f.Other.TelegramUserId, f.Clock.Now, default, true));
        var campCommunity = new OyinQCommunity { Key = "camp-gate", Name = "Кэмп", Mode = BotMode.Camp, TimeZoneId = "Asia/Almaty", IsActive = true };
        f.Db.Camps.Add(new() { BotChat = campCommunity, Status = CampStatus.Active, StartsAtUtc = f.Clock.Now, EndsAtUtc = f.Clock.Now.AddDays(2) });
        var campGathering = new GameGathering { PublicId = Guid.NewGuid(), Community = campCommunity, OrganizerParticipant = f.Me,
            StartsAtUtc = f.Clock.Now.AddHours(2), Status = GatheringStatus.Recruiting, MinimumPlayers = 1, DesiredPlayers = 2, MaximumPlayers = 3,
            GameSnapshotJson = g.GameSnapshotJson, CreatedAt = f.Clock.Now, UpdatedAt = f.Clock.Now };
        f.Db.GameGatherings.Add(campGathering); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.JoinAsync(campGathering.PublicId, "camp-gate", f.Other.TelegramUserId, f.Clock.Now, default, true));
        Assert.Empty(campGathering.Participants);
    }

    [Fact]
    public async Task Play_RequiresExplicitConfirmation_CorrectedRosterAndRevision_PreservesAttendance()
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("club", f.Clock.Now.AddHours(-3)); g.Status = GatheringStatus.Completed;
        g.Participants.Add(new() { Participant = f.Other, Status = GatheringParticipationStatus.Confirmed, AttendanceOutcome = AttendanceOutcome.Unknown });
        await f.Db.SaveChangesAsync();
        Assert.Empty(f.Db.GatheringPlayRecords);
        var service = new GatheringPlayService(f.Db, f.Clock);
        var command = new RecordPlayCommand(true, f.Clock.Now.AddMinutes(-10), 120, [f.Me.PublicId], [], 0);
        var record = await service.SaveAsync(g.PublicId, "club", f.Me.Id, command, default);
        var originalSnapshot = g.GameSnapshotJson;
        Assert.Equal(f.Me.Id, Assert.Single(record!.Players).ParticipantId);
        Assert.Equal(AttendanceOutcome.Unknown, g.Participants.Single().AttendanceOutcome);
        Assert.Equal(GatheringStatus.Completed, g.Status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(g.PublicId, "club", f.Other.Id, command, default));
        await Assert.ThrowsAsync<GatheringPlayConflictException>(() => service.SaveAsync(g.PublicId, "club", f.Me.Id, command with { WasPlayed = false, ExpectedRevision = 1 }, default));
        Assert.Equal(originalSnapshot, g.GameSnapshotJson);
        Assert.Single(f.Db.GatheringPlayRecords);

    }

    [Fact]
    public async Task Play_RejectsOutsidersAndUncompletedGatherings_AndFutureEnd()
    {
        await using var f = new PlanningFixture(); var g = f.Gathering("club", f.Clock.Now.AddHours(-2));
        await f.Db.SaveChangesAsync();
        var service = new GatheringPlayService(f.Db, f.Clock);
        var command = new RecordPlayCommand(true, f.Clock.Now, null, [f.Me.PublicId], [], 0);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(g.PublicId, "club", f.Me.Id, command, default));
        g.Status = GatheringStatus.Completed; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(g.PublicId, "club", f.Other.Id, command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(g.PublicId, "club", f.Me.Id, command with { EndedAtUtc = f.Clock.Now.AddHours(1) }, default));
        Assert.Empty(f.Db.GatheringPlayRecords);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.org/play")]
    [InlineData("//example.org/play")]
    [InlineData("https://user:password@example.org/play")]
    [InlineData("file:///tmp/play")]
    public void Play_RejectsUnsafeExternalLinks(string value) => Assert.Throws<ArgumentException>(() => ExternalPlayReferenceService.Normalize(value));

    [Fact]
    public void BgStats_UsesOfficialUtcEndTimeOpaqueStableIds_AndEscapesJson()
    {
        var id = Guid.NewGuid(); var player = Guid.NewGuid();
        var play = new PortablePlay(id, "Игра & Друзья", 42, new DateTimeOffset(2026, 9, 4, 22, 0, 0, TimeSpan.FromHours(5)), "Клуб", 90,
            [new(player, "Имя & Другое")], [new(77, "Дополнение")]);
        var link = BgStatsPlayExportAdapter.Build(play);
        Assert.Equal(link, BgStatsPlayExportAdapter.Build(play));
        var data = JsonDocument.Parse(Uri.UnescapeDataString(link.Split("?data=")[1])).RootElement;
        Assert.Equal("2026-09-04 17:00:00", data.GetProperty("playDate").GetString());
        Assert.Equal(id.ToString("N"), data.GetProperty("sourcePlayId").GetString());
        Assert.Equal(player.ToString("N"), data.GetProperty("players")[0].GetProperty("sourcePlayerId").GetString());
        Assert.Equal("Игра & Друзья", data.GetProperty("game").GetProperty("name").GetString());
        Assert.Contains("Дополнение", data.GetProperty("comments").GetString());
        Assert.DoesNotContain("Telegram", data.GetRawText());
    }

    private static Camp Camp() => new() { BotChat = new() { Key = "camp", TimeZoneId = "Asia/Almaty" }, Status = CampStatus.Active,
        StartsAtUtc = CommunityTime.ParseLocal("2026-09-04T09:00", "Asia/Almaty"), EndsAtUtc = CommunityTime.ParseLocal("2026-09-06T09:00", "Asia/Almaty") };
}

internal sealed class PlanningClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
internal sealed class PlanningFixture : IAsyncDisposable
{
    public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    public PlanningClock Clock { get; } = new();
    public Participant Me { get; } = new() { Id = 1, TelegramUserId = 12111111, DisplayName = "Первый" };
    public Participant Other { get; } = new() { Id = 2, TelegramUserId = 12222222, DisplayName = "Второй" };
    public PlanningFixture() { Db.Participants.AddRange(Me, Other); }
    public GameGathering Gathering(string key, DateTimeOffset start, long bggId = 42)
    {
        var community = Db.OyinQCommunities.Local.SingleOrDefault(x => x.Key == key) ?? new OyinQCommunity { Key = key, Name = key, Mode = BotMode.Club, TimeZoneId = "Asia/Almaty", IsActive = true };
        if (Db.Entry(community).State == EntityState.Detached) { Db.OyinQCommunities.Add(community); Db.Clubs.Add(new() { BotChat = community, CollectionJson = ClubCollectionSerializer.Serialize(new(2, [])) }); }
        var g = new GameGathering { PublicId = Guid.NewGuid(), Community = community, CommunityKey = key,
            OrganizerParticipant = Me, OrganizerParticipantId = Me.Id, StartsAtUtc = start, Status = GatheringStatus.Recruiting,
            MinimumPlayers = 2, DesiredPlayers = 3, MaximumPlayers = 4, CreatedAt = Clock.Now, UpdatedAt = Clock.Now,
            GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(new(GatheringGameSnapshot.CurrentVersion, bggId, "Игра", null, null, 1, 8, null, [], "test", [])) };
        Db.GameGatherings.Add(g); return g;
    }
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
