using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.MiniApp;

internal static class BggEndpoints
{
    public static RouteGroupBuilder MapBggEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/bgg/search", SearchAsync);
        group.MapGet("/bgg/game", GetGameAsync);
        return group;
    }

    private static async Task<IResult> SearchAsync(HttpRequest request, string query,
        TelegramMiniAppAuthenticator authenticator, IBoardGameGeekClient client,
        IOptions<BggOptions> options, CancellationToken cancellationToken)
    {
        if (MiniAppEndpointSupport.Authenticate(request, authenticator) is null) return Results.Unauthorized();
        if (!options.Value.IsAvailable) return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно отключён.", 503);
        try { return Results.Ok(await client.SearchAsync(query, cancellationToken)); }
        catch (HttpRequestException exception) { return MiniAppEndpointSupport.Problem("bgg_unavailable", exception.Message, 503); }
    }

    private static async Task<IResult> GetGameAsync(HttpRequest request, string input,
        TelegramMiniAppAuthenticator authenticator, IBoardGameGeekClient client,
        IOptions<BggOptions> options, CancellationToken cancellationToken)
    {
        if (MiniAppEndpointSupport.Authenticate(request, authenticator) is null) return Results.Unauthorized();
        if (!options.Value.IsAvailable) return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно отключён.", 503);
        var id = BggGameUrlParser.Parse(input) ?? (long.TryParse(input, out var parsed) && parsed > 0 ? parsed : null);
        if (id is null) return MiniAppEndpointSupport.Problem("validation", "Вставьте ссылку BGG или выберите игру.");
        try
        {
            var details = await client.GetGameDetailsAsync(id.Value, cancellationToken);
            return details is null ? Results.NotFound() : Results.Ok(details);
        }
        catch (HttpRequestException exception) { return MiniAppEndpointSupport.Problem("bgg_unavailable", exception.Message, 503); }
    }
}
