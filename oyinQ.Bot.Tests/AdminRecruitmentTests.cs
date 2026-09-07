using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Tests;

public sealed class AdminRecruitmentTests
{
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
    [InlineData("far")]
    public async Task AdminCannotQueueUnavailableCampOrGatherings(string reason)
    {
        await using var f = new PlanningFixture();
        var g = f.Gathering("camp", f.Clock.Now.AddHours(reason == "far" ? 37 : 2));
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

    private sealed class Verifier : ITelegramChatAdministratorVerifier
    {
        public bool Allowed = true;
        public Task<bool> IsAdministratorAsync(long chatId, long userId, CancellationToken ct) => Task.FromResult(Allowed);
        public Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(long chatId, CancellationToken ct) => throw new NotSupportedException();
    }
}
