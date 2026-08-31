using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Catalog;

namespace oyinQ.Bot.Features.MiniApp;

internal static class CommunityEndpoints
{
    public static RouteGroupBuilder MapCommunityEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/communities", GetCommunitiesAsync);
        group.MapGet("/capabilities", GetCapabilitiesAsync);
        group.MapGet("/games", GetGamesAsync);
        return group;
    }

    private static async Task<IResult> GetCommunitiesAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        IAdministratorStore administrators, CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Unauthorized();
        var communities = await resolver.ResolveAuthorizedAsync(identity.TelegramUserId, cancellationToken);
        var isAdmin = await administrators.IsAdministratorAsync(identity.TelegramUserId, cancellationToken);
        return Results.Ok(new
        {
            IsAdministrator = isAdmin,
            Communities = communities.Select(x => new { x.Key, x.Name, Mode = x.Mode.ToString(), x.TimeZoneId })
        });
    }

    private static IResult GetCapabilitiesAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, IOptions<BggOptions> bggOptions)
    {
        if (MiniAppEndpointSupport.Authenticate(request, authenticator) is null) return Results.Unauthorized();
        return Results.Ok(new
        {
            BoardGameGeekAvailable = bggOptions.Value.IsAvailable,
            BoardGameGeekUnavailableReason = bggOptions.Value.IsAvailable ? null : "BGG временно отключён администратором.",
            PreparedPeerSelection = true
        });
    }

    private static async Task<IResult> GetGamesAsync(HttpRequest request, string community,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, EffectiveCampCatalogService campCatalog,
        CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator,
            resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        if (access.Community.Mode == BotMode.Club)
        {
            var json = await dbContext.Clubs.AsNoTracking().Where(x => x.BotChatKey == community)
                .Select(x => x.CollectionJson).SingleAsync(cancellationToken);
            return Results.Ok(ClubCollectionSerializer.Deserialize(json).Games.Select(PresentGame));
        }
        var participantId = await dbContext.Participants.AsNoTracking()
            .Where(x => x.TelegramUserId == access.Identity.TelegramUserId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        return Results.Ok((await campCatalog.LoadAsync(community, participantId, cancellationToken))
            .Select(x => PresentGame(x.Game)));
    }

    private static object PresentGame(ClubCollectionGame game)
    {
        var metadata = BggTaxonomyCatalog.Present(game);
        return new { game.BggId, game.Name, game.ThumbnailImageUrl, game.ImageUrl, game.Description,
            game.YearPublished, game.MinPlayers, game.MaxPlayers, game.BestPlayers,
            game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge, game.Type,
            metadata.TypeName, metadata.CategoryNames, metadata.MechanicNames,
            game.CategoryItems, game.Mechanics, game.Expansions };
    }
}
