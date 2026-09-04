using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.Telegram;

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
        IAdminAuthorizationService authorization, TelegramCommunityPhotoService photos,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Unauthorized();
        var communities = await resolver.ResolveAuthorizedAsync(identity.TelegramUserId, cancellationToken);
        var canOpenAdminPanel = await authorization.CanOpenAdminPanelAsync(identity.TelegramUserId, cancellationToken);
        var items = new List<object>();
        foreach (var community in communities)
            items.Add(new
            {
                community.Key, community.Name, Mode = community.Mode.ToString(), community.TimeZoneId,
                community.StartDate, community.EndDate, community.StartsAtUtc, community.EndsAtUtc,
                AvatarUrl = await photos.GetDataUrlAsync(community.TelegramChatId, cancellationToken)
            });
        return Results.Ok(new
        {
            CanOpenAdminPanel = canOpenAdminPanel,
            IsSuperAdmin = authorization.IsSuperAdmin(identity.TelegramUserId),
            Communities = items
        });
    }

    private static IResult GetCapabilitiesAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, IOptions<BggOptions> bggOptions)
    {
        if (MiniAppEndpointSupport.Authenticate(request, authenticator) is null) return Results.Unauthorized();
        return Results.Ok(new
        {
            BoardGameGeekAvailable = bggOptions.Value.IsAvailable,
            BoardGameGeekUnavailableReason = bggOptions.Value.IsAvailable ? null : "BGG временно отключён администратором."
        });
    }

    private static async Task<IResult> GetGamesAsync(HttpRequest request, string community,
        TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver,
        oyinQ.Bot.Features.Catalog.GameCatalogService catalog, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator,
            resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        var games = await catalog.LoadAsync(community, access.Community.Mode, access.Identity.TelegramUserId, cancellationToken);
        return Results.Ok(games.Select(x => PresentGame(x.Game)));
    }

    private static object PresentGame(ClubCollectionGame game)
    {
        var metadata = BggTaxonomyCatalog.Present(game);
        var players = PlayerCountRange.Normalize(game.MinPlayers, game.MaxPlayers);
        return new { game.BggId, game.Name, game.ThumbnailImageUrl, game.ImageUrl, game.Description,
            game.YearPublished, MinPlayers = players.Minimum, MaxPlayers = players.Maximum,
            PlayerRangeDefaulted = players.WasDefaulted, game.BestPlayers,
            game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge, game.Type,
            metadata.TypeName, metadata.TypeNames, metadata.CategoryNames, metadata.MechanicNames,
            game.CategoryItems, game.Mechanics, game.Expansions };
    }
}
