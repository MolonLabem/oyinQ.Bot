using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.MiniApp;

internal static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/catalog", ListAsync);
        group.MapGet("/catalog/demand", DemandAsync);
        group.MapGet("/catalog/demand/{bggId:long}", DemandGameAsync);
        group.MapGet("/catalog/{bggId:long}", DetailsAsync);
        return group;
    }

    private static async Task<IResult> DemandAsync(HttpRequest request, string community,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver, GameCatalogService service, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, ct);
        if (access is null) return Results.Forbid();
        return Results.Ok(await service.DemandAsync(community, access.Community.Mode, access.Identity.TelegramUserId, ct));
    }

    private static async Task<IResult> DemandGameAsync(HttpRequest request, string community, long bggId,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver, GameCatalogService service, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, ct);
        if (access == null) return Results.Forbid();
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        try { return Results.Ok(await service.DemandGameAsync(community, access.Community.Mode, access.Identity.TelegramUserId, bggId, ct)); }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }

    private static async Task<IResult> ListAsync(HttpRequest request, string community, string? search,
        int? players, string? types, string? categories, string? sort, string? ownership, string? availability,
        string? planning, string? providers, string? complexities, string? mechanics, int? maxDurationMinutes, DateOnly? attendanceDate, bool? countOnly,
        int? fromYear, int? toYear, string? playerCountMode,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GameCatalogService service, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        var countMode = playerCountMode switch
        {
            null or "supported" => CatalogPlayerCountMode.Supported,
            "best" => CatalogPlayerCountMode.Best,
            _ => (CatalogPlayerCountMode?)null
        };
        if (countMode is null) return MiniAppEndpointSupport.Problem("validation", "Неизвестный режим количества игроков.");
        var parsedTypes = (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Enum.TryParse<GameType>(value, true, out var parsed) ? parsed : (GameType?)null)
            .Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        var categoryIds = (categories ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(value, out var parsed) ? parsed : 0).Where(value => value > 0).ToArray();
        var providerParticipantIds = (providers ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(value, out var parsed) ? parsed : 0).Where(value => value > 0).Distinct().ToArray();
        var levels = (complexities ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Enum.TryParse<GameComplexity>(x, true, out var level) && Enum.IsDefined(level) ? level : (GameComplexity?)null).ToArray();
        if (levels.Any(x => x is null)) return MiniAppEndpointSupport.Problem("validation", "Неизвестная сложность игры.");
        var mechanicIds = (mechanics ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => long.TryParse(x, out var id) ? id : 0).Where(x => x > 0).Distinct().ToArray();
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        try { var result = await service.ListAsync(community, access.Community.Mode, access.Identity.TelegramUserId,
            new CatalogQuery(search, players, parsedTypes, categoryIds, sort, ownership, availability, planning,
                providerParticipantIds, levels.Select(x => x!.Value).ToArray(), maxDurationMinutes, mechanicIds, attendanceDate,
                fromYear, toYear, countMode.Value), cancellationToken, countOnly == true);
            return countOnly == true ? Results.Ok(new { result.Total }) : Results.Ok(result); }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }

    private static async Task<IResult> DetailsAsync(HttpRequest request, string community, long bggId, DateOnly? attendanceDate,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GameCatalogService service, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        try { return Results.Ok(await service.DetailsAsync(community, access.Community.Mode,
            access.Identity.TelegramUserId, bggId, cancellationToken, attendanceDate)); }
        catch (GameNotInCollectionException exception)
        {
            return MiniAppEndpointSupport.Problem("game_not_in_collection", exception.Message,
                StatusCodes.Status404NotFound);
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }
}
