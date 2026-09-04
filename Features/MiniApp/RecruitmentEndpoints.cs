using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Features.MiniApp;

internal static class RecruitmentEndpoints
{
    internal sealed record WishRequest(string CommunityKey, bool Wished);
    internal sealed record CooldownSettings(int Hours);

    public static void MapRecruitmentEndpoints(this RouteGroupBuilder group)
    {
        group.MapPut("/wishlist/{bggId:long}", SetWishAsync);
        group.MapGet("/wishlist/{bggId:long}", GetWishAsync);
        group.MapPost("/gatherings/{id:guid}/recruitment", RequestAsync);
        group.MapGet("/admin/communities/{key}/recruitment", GetSettingsAsync);
        group.MapPut("/admin/communities/{key}/recruitment", SetSettingsAsync);
    }

    private static async Task<IResult> GetWishAsync(HttpRequest request, long bggId, string community,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, auth, resolver, ct);
        if (access is null) return Results.Forbid();
        return Results.Ok(new { Wished = await db.GameWishes.AnyAsync(x => x.CommunityKey == community && x.BggId == bggId
            && x.Participant.TelegramUserId == access.Identity.TelegramUserId, ct) });
    }

    private static async Task<IResult> SetWishAsync(HttpRequest request, long bggId, WishRequest body,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, GameWishService service, AppDbContext db, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey, auth, resolver, ct);
        if (access is null) return Results.Forbid();
        try
        {
            var id = await db.Participants.Where(x => x.TelegramUserId == access.Identity.TelegramUserId).Select(x => x.Id).SingleAsync(ct);
            await service.SetAsync(body.CommunityKey, id, bggId, body.Wished, ct);
            return Results.Ok(new { body.Wished });
        }
        catch (HttpRequestException) { return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно недоступен. Попробуйте позже.", 503); }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }

    private static async Task<IResult> RequestAsync(HttpRequest request, Guid id, GatheringActionRequest body,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, RecruitmentDigestService service, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey, auth, resolver, ct);
        if (access is null) return Results.Forbid();
        try
        {
            var participantId = await db.Participants.Where(x => x.TelegramUserId == access.Identity.TelegramUserId).Select(x => x.Id).SingleAsync(ct);
            return Results.Ok(await service.RequestAsync(body.CommunityKey, id, participantId, ct));
        }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }

    private static async Task<IResult> GetSettingsAsync(HttpRequest request, string key,
        TelegramMiniAppAuthenticator auth, IAdminAuthorizationService authorization, AppDbContext db, CancellationToken ct)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, auth);
        if (identity is null || !await authorization.CanAdministerCommunityAsync(identity.TelegramUserId, key, ct)) return Results.Forbid();
        return Results.Ok(new CooldownSettings(await db.OyinQCommunities.Where(x => x.Key == key).Select(x => x.RecruitmentCooldownHours).SingleAsync(ct)));
    }

    private static async Task<IResult> SetSettingsAsync(HttpRequest request, string key, CooldownSettings body,
        TelegramMiniAppAuthenticator auth, IAdminAuthorizationService authorization, RecruitmentDigestService service, CancellationToken ct)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, auth);
        if (identity is null || !await authorization.CanAdministerCommunityAsync(identity.TelegramUserId, key, ct)) return Results.Forbid();
        try { await service.SetCooldownAsync(key, body.Hours, ct); return Results.Ok(body); }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }
}
