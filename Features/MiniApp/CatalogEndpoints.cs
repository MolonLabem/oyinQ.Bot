using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.MiniApp;

internal static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/catalog", ListAsync);
        group.MapGet("/catalog/{bggId:long}", DetailsAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(HttpRequest request, string community, string? search,
        int? players, string? types, string? categories, string? sort,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GameCatalogService service, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        var parsedTypes = (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Enum.TryParse<GameType>(value, true, out var parsed) ? parsed : (GameType?)null)
            .Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        var categoryIds = (categories ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(value, out var parsed) ? parsed : 0).Where(value => value > 0).ToArray();
        return Results.Ok(await service.ListAsync(community, access.Community.Mode, access.Identity.TelegramUserId,
            new CatalogQuery(search, players, parsedTypes, categoryIds, sort), cancellationToken));
    }

    private static async Task<IResult> DetailsAsync(HttpRequest request, string community, long bggId,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GameCatalogService service, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        try { return Results.Ok(await service.DetailsAsync(community, access.Community.Mode,
            access.Identity.TelegramUserId, bggId, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }
}
