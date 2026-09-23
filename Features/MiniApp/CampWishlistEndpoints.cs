using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.MiniApp;

internal static class CampWishlistEndpoints
{
    internal sealed record ActionRequest(string Action, long BggId = 0, Guid? Owner = null, DateOnly[]? Dates = null,
        bool? ShareCollection = null, bool? ShareWishes = null);

    public static void MapCampWishlistEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/camp-wishlist", ReadAsync);
        group.MapPost("/camp-wishlist", ActAsync);
    }

    private static async Task<long?> Authorize(HttpRequest request, string community, TelegramMiniAppAuthenticator auth,
        CommunityContextResolver resolver, AppDbContext db, CancellationToken ct)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, auth, resolver, ct);
        if (access?.Community.Mode != BotMode.Camp) return null;
        return await db.Participants.Where(x => x.TelegramUserId == access.Identity.TelegramUserId).Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
    }

    private static async Task<IResult> ReadAsync(HttpRequest request, string community, string? view, long? game,
        Guid? person, string? search, string? mode, string? sort, bool? needsBox, bool? withoutGathering, bool? owned,
        bool? showDeclined, int? page, TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver,
        AppDbContext db, CampWishlistService service, CancellationToken ct)
    {
        var actor = await Authorize(request, community, auth, resolver, db, ct);
        if (actor == null) return Results.Forbid();
        try
        {
            if (view == "profile") return Results.Ok(await service.ProfileAsync(community, actor.Value, person, search, mode, page ?? 1, ct));
            if (view == "participants") return Results.Ok(await service.ParticipantsAsync(community, actor.Value, search, page ?? 1, ct));
            if (view == "incoming") return Results.Ok(await service.IncomingAsync(community, actor.Value, ct));
            if (view == "outgoing") return Results.Ok(await service.OutgoingAsync(community, actor.Value, ct));
            if (game is { } id) return Results.Ok(await service.DetailsAsync(community, actor.Value, id, ct));
            return Results.Ok(await service.ListAsync(community, actor.Value, new(mode ?? "all", search, needsBox == true,
                withoutGathering == true, owned == true, sort ?? "demand", page ?? 1, showDeclined == true), ct));
        }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }

    private static async Task<IResult> ActAsync(HttpRequest request, string community, ActionRequest body,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, CampWishlistService service, CancellationToken ct)
    {
        var actor = await Authorize(request, community, auth, resolver, db, ct);
        if (actor == null) return Results.Forbid();
        try { await service.ActAsync(community, actor.Value, body.BggId, body.Action, body.Owner, body.Dates,
            body.ShareCollection, body.ShareWishes, ct); return Results.Ok(new { Saved = true }); }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }
}
