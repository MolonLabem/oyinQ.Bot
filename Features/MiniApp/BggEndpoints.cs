using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
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
        IOptions<BggOptions> options, ILogger<BoardGameGeekClient> logger,
        CancellationToken cancellationToken)
    {
        if (MiniAppEndpointSupport.Authenticate(request, authenticator) is null) return Results.Unauthorized();
        if (!options.Value.IsAvailable) return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно отключён.", 503);
        try { return Results.Ok(await client.SearchAsync(query, cancellationToken)); }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "BGG search failed for query length {QueryLength}.", query.Length);
            return MiniAppEndpointSupport.Problem("bgg_unavailable",
                "BGG временно недоступен. Сохранённые игры и сборы продолжают работать.", 503);
        }
    }

    private static async Task<IResult> GetGameAsync(HttpRequest request, string input,
        TelegramMiniAppAuthenticator authenticator, IBoardGameGeekClient client,
        IOptions<BggOptions> options, ILogger<BoardGameGeekClient> logger,
        CancellationToken cancellationToken)
    {
        if (MiniAppEndpointSupport.Authenticate(request, authenticator) is null) return Results.Unauthorized();
        if (!options.Value.IsAvailable) return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно отключён.", 503);
        var id = BggGameUrlParser.Parse(input) ?? (long.TryParse(input, out var parsed) && parsed > 0 ? parsed : null);
        if (id is null) return MiniAppEndpointSupport.Problem("validation", "Вставьте ссылку BGG или выберите игру.");
        try
        {
            var details = await client.GetGameDetailsAsync(id.Value, cancellationToken);
            if (details is null) return Results.NotFound();
            var game = details.Game;
            var collectionGame = new ClubCollectionGame(game.BggId!.Value, game.Name, game.ThumbnailImageUrl,
                game.ImageUrl, game.MinPlayers, game.MaxPlayers, game.BestPlayers, [], game.Types,
                game.Categories, game.Description, game.YearPublished, game.MinPlayTimeMinutes,
                game.MaxPlayTimeMinutes, game.MinAge, game.Type, game.Subdomains, game.CategoryItems,
                game.Mechanics, game.OriginalName);
            var metadata = BggTaxonomyCatalog.Present(collectionGame);
            var players = PlayerCountRange.Normalize(collectionGame.MinPlayers, collectionGame.MaxPlayers);
            return Results.Ok(new
            {
                Game = new { collectionGame.BggId, collectionGame.Name, collectionGame.OriginalName,
                    collectionGame.ThumbnailImageUrl,
                    collectionGame.ImageUrl, MinPlayers = players.Minimum, MaxPlayers = players.Maximum,
                    PlayerRangeDefaulted = players.WasDefaulted,
                    collectionGame.BestPlayers, collectionGame.Description, collectionGame.YearPublished,
                    collectionGame.MinPlayTimeMinutes, collectionGame.MaxPlayTimeMinutes, collectionGame.MinAge,
                    collectionGame.Type, metadata.TypeName, metadata.TypeNames, metadata.CategoryNames,
                    metadata.MechanicNames, collectionGame.CategoryItems, collectionGame.Mechanics },
                details.Expansions
            });
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "BGG game detail fetch failed for game {BggId}.", id.Value);
            return MiniAppEndpointSupport.Problem("bgg_unavailable",
                "Не удалось загрузить данные BGG. Попробуйте ещё раз позже.", 503);
        }
    }
}
