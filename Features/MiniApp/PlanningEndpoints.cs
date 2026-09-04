using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Features.MiniApp;

internal static class PlanningEndpoints
{
    public static RouteGroupBuilder MapPlanningEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/profile/dashboard", PersonalAsync);
        group.MapGet("/gatherings/dashboard", OrganizerAsync);
        group.MapGet("/catalog/{bggId:long}/provider", ProviderAsync);
        group.MapPost("/gatherings/{id:guid}/bring", BringAsync);
        return group;
    }
    private static async Task<IResult> PersonalAsync(HttpRequest request,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, GatheringDashboardService service, CancellationToken ct)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, auth);
        if (identity is null) return Results.Unauthorized();
        var p = await db.Participants.SingleAsync(x => x.TelegramUserId == identity.TelegramUserId, ct);
        var keys = (await resolver.ResolveAuthorizedAsync(p.TelegramUserId, ct)).Select(x => x.Key).ToArray();
        return Results.Ok(await service.PersonalAsync(p.Id, keys, ct));
    }
    private static async Task<IResult> OrganizerAsync(HttpRequest request, string community,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, IAdminAuthorizationService authorization,
        AppDbContext db, GatheringDashboardService service, CancellationToken ct)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, auth);
        if (identity is null) return Results.Unauthorized();
        var admin = await authorization.CanAdministerCommunityAsync(identity.TelegramUserId, community, ct);
        if (!admin && await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, auth, resolver, ct) is null) return Results.Forbid();
        var p = await db.Participants.SingleAsync(x => x.TelegramUserId == identity.TelegramUserId, ct);
        return Results.Ok(await service.OrganizerAsync(p.Id, community, admin, ct));
    }
    private static async Task<IResult> ProviderAsync(HttpRequest request, string community, long bggId, string? startsAtLocal,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, GameProviderService service, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, auth, resolver, ct);
        if (access is null) return Results.Forbid();
        try
        {
            var p = await db.Participants.SingleAsync(x => x.TelegramUserId == access.Identity.TelegramUserId, ct);
            var instant = string.IsNullOrEmpty(startsAtLocal) ? (DateTimeOffset?)null : CommunityTime.ParseLocal(startsAtLocal, access.Community.TimeZoneId);
            return Results.Ok(await service.ForGameAsync(community, bggId, p.Id, instant, ct));
        }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }
    private static async Task<IResult> BringAsync(HttpRequest request, Guid id, CampMutationRequest body,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, GameProviderService service, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey, auth, resolver, ct);
        if (access is null) return Results.Forbid();
        try
        {
            var p = await db.Participants.SingleAsync(x => x.TelegramUserId == access.Identity.TelegramUserId, ct);
            var g = await db.GameGatherings.SingleOrDefaultAsync(x => x.PublicId == id && x.CommunityKey == body.CommunityKey, ct);
            if (g is null) return Results.NotFound();
            await service.BringAsync(g, p.Id, ct);
            return Results.NoContent();
        }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }
}
