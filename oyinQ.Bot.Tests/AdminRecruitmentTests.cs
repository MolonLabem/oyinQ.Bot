using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class AdminRecruitmentTests
{
    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 129600, true)]
    [InlineData(true, 129600, true)]
    [InlineData(false, 129601, false)]
    [InlineData(true, 129601, true)]
    [InlineData(false, 133200, false)]
    [InlineData(true, 133200, true)]
    public async Task AdminRequestAndCandidateQueryShareTimeBoundaries(bool camp, int seconds, bool eligible)
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("community", f.Clock.Now.AddSeconds(seconds));
        g.Community.Mode = camp ? BotMode.Camp : BotMode.Club;
        if (camp) f.Db.Camps.Add(new() { BotChat = g.Community, Status = CampStatus.Active,
            StartsAtUtc = f.Clock.Now.AddDays(-1), EndsAtUtc = f.Clock.Now.AddDays(5) });
        await f.Db.SaveChangesAsync();
        var candidates = await GatheringRecruitment.LoadCandidatesAsync(f.Db, "community", f.Clock.Now, camp, default);
        Assert.Equal(eligible ? 1 : 0, candidates.Length);
        Assert.Equal(eligible, GatheringRecruitment.IsRelevant(g, f.Clock.Now, camp));
        var auth = new AdminAuthorizationService(f.Db, new Verifier(),
            Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = new HashSet<long> { 99 } }), f.Clock);
        var service = new RecruitmentDigestService(f.Db, f.Clock);
        if (eligible) Assert.True((await service.RequestAsAdminAsync("community", 99, auth, default)).Queued);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestAsAdminAsync("community", 99, auth, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthorizedCampAdmin_UsesSharedOrganizerQueueAndCooldown(bool superAdmin)
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("camp", f.Clock.Now.AddHours(2));
        g.Community.Mode = BotMode.Camp;
        f.Db.Camps.Add(new() { BotChat = g.Community, Status = CampStatus.Active,
            StartsAtUtc = f.Clock.Now.AddDays(-1), EndsAtUtc = f.Clock.Now.AddDays(2) });
        f.Db.ChatAdminPermissions.Add(new() { Community = g.Community, TelegramUserId = 99 });
        await f.Db.SaveChangesAsync();
        var verifier = new Verifier();
        var auth = new AdminAuthorizationService(f.Db, verifier,
            Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = superAdmin ? new HashSet<long> { 99 } : new HashSet<long>() }), f.Clock);
        var service = new RecruitmentDigestService(f.Db, f.Clock);
        Assert.True((await service.RequestAsAdminAsync("camp", 99, auth, default)).Queued);
        Assert.Equal("camp", Assert.Single(f.Db.RecruitmentDigests).CommunityKey);
        Assert.False((await service.RequestAsync("camp", g.PublicId, f.Me.Id, default)).Queued);
        Assert.False((await service.RequestAsAdminAsync("camp", 99, auth, default)).Queued);
        if (!superAdmin)
        {
            verifier.Allowed = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsAdminAsync("camp", 99, auth, default));
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsAdminAsync("camp", 100, auth, default));
        Assert.Single(f.Db.RecruitmentDigests);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("ended")]
    [InlineData("deleted")]
    [InlineData("full")]
    public async Task AdminCannotQueueUnavailableCampOrGatherings(string reason)
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("camp", f.Clock.Now.AddHours(2));
        g.Community.Mode = BotMode.Camp;
        if (reason == "deleted") g.Community.DeletedAt = f.Clock.Now;
        if (reason == "full") g.Guests = [new(), new(), new()];
        f.Db.Camps.Add(new() { BotChat = g.Community,
            Status = reason == "closed" ? CampStatus.Closed : CampStatus.Active,
            StartsAtUtc = f.Clock.Now.AddDays(-1), EndsAtUtc = reason == "ended" ? f.Clock.Now : f.Clock.Now.AddDays(2) });
        await f.Db.SaveChangesAsync();
        var auth = new AdminAuthorizationService(f.Db, new Verifier(),
            Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = new HashSet<long> { 99 } }), f.Clock);
        var service = new RecruitmentDigestService(f.Db, f.Clock);
        if (reason == "deleted")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsAdminAsync("camp", 99, auth, default));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestAsAdminAsync("camp", 99, auth, default));
        Assert.Empty(f.Db.RecruitmentDigests);
        Assert.Null(g.Community.LastRecruitmentDigestAt);
    }

    [Theory]
    [InlineData(BotMode.Camp)]
    [InlineData(BotMode.Club)]
    public async Task OnlyCampAdminCanRequestBeyond36Hours(BotMode mode)
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("community", f.Clock.Now.AddDays(7));
        g.Community.Mode = mode;
        if (mode == BotMode.Camp)
            f.Db.Camps.Add(new() { BotChat = g.Community, Status = CampStatus.Active,
                StartsAtUtc = f.Clock.Now.AddDays(6), EndsAtUtc = f.Clock.Now.AddDays(9) });
        await f.Db.SaveChangesAsync();
        var auth = new AdminAuthorizationService(f.Db, new Verifier(),
            Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = new HashSet<long> { 99 } }), f.Clock);
        var service = new RecruitmentDigestService(f.Db, f.Clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestAsync("community", g.PublicId, f.Me.Id, default));
        if (mode == BotMode.Club)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestAsAdminAsync("community", 99, auth, default));
            Assert.Empty(f.Db.RecruitmentDigests);
            return;
        }
        Assert.True((await service.RequestAsAdminAsync("community", 99, auth, default)).Queued);
        var row = Assert.Single(f.Db.RecruitmentDigests);
        Assert.True(row.IncludeAllUpcoming);
        Assert.Equal(1, RecruitmentDigestFormatter.Build([g], "community", "UTC", f.Clock.Now, "test_bot", row.IncludeAllUpcoming).Total);
        Assert.Equal(0, RecruitmentDigestFormatter.Build([g], "community", "UTC", f.Clock.Now, "test_bot").Total);
        Assert.False((await service.RequestAsAdminAsync("community", 99, auth, default)).Queued);
    }

    private sealed class Verifier : ITelegramChatAdministratorVerifier
    {
        public bool Allowed = true;
        public Task<bool> IsAdministratorAsync(long chatId, long userId, CancellationToken ct) => Task.FromResult(Allowed);
        public Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(long chatId, CancellationToken ct) => throw new NotSupportedException();
    }
}
