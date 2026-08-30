using System.Text;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record ReplaceClubCollectionRequest(long ExpectedRevision, ClubCollectionDocument Document);
internal sealed record AddClubGameRequest(long ExpectedRevision, string BggInput,
    IReadOnlyCollection<long>? ExpansionBggIds);

internal static class ClubEndpoints
{
    public static RouteGroupBuilder MapClubEndpoints(this RouteGroupBuilder group)
    {
        var clubs = group.MapGroup("/admin/clubs");
        clubs.MapGet("/{clubId:long}/collection", GetAsync);
        clubs.MapGet("/{clubId:long}/collection/export", ExportAsync);
        clubs.MapPut("/{clubId:long}/collection", ReplaceAsync);
        clubs.MapPost("/{clubId:long}/games", AddAsync);
        clubs.MapDelete("/{clubId:long}/games/{bggId:long}", RemoveAsync);
        clubs.MapPost("/{clubId:long}/metadata-refresh", QueueMetadataRefreshAsync);
        clubs.MapGet("/{clubId:long}/metadata-refresh/{publicId:guid}", GetMetadataRefreshAsync);
        return group;
    }

    private static async Task<TelegramMiniAppIdentity?> AdminAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        CancellationToken cancellationToken) => await MiniAppEndpointSupport.AuthenticateAdminAsync(
            request, authenticator, administrators, cancellationToken);

    private static async Task<IResult> GetAsync(HttpRequest request, long clubId,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        ClubCollectionService service, CancellationToken cancellationToken)
    {
        if (await AdminAsync(request, authenticator, administrators, cancellationToken) is null) return Results.Forbid();
        try { return Results.Ok(await service.GetAsync(clubId, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> ExportAsync(HttpRequest request, long clubId,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        ClubCollectionService service, CancellationToken cancellationToken)
    {
        if (await AdminAsync(request, authenticator, administrators, cancellationToken) is null) return Results.Forbid();
        try
        {
            var state = await service.GetAsync(clubId, cancellationToken);
            var json = ClubCollectionSerializer.Serialize(state.Collection);
            return Results.File(Encoding.UTF8.GetBytes(json), "application/json",
                $"club-{clubId}-collection-r{state.Revision}.json");
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> ReplaceAsync(HttpRequest request, long clubId,
        ReplaceClubCollectionRequest body, TelegramMiniAppAuthenticator authenticator,
        IAdministratorStore administrators, ClubCollectionService service,
        CancellationToken cancellationToken)
    {
        if (await AdminAsync(request, authenticator, administrators, cancellationToken) is null) return Results.Forbid();
        try
        {
            await service.ReplaceAsync(clubId, body.Document, body.ExpectedRevision,
                DateTimeOffset.UtcNow, cancellationToken);
            return Results.Ok(await service.GetAsync(clubId, cancellationToken));
        }
        catch (ClubCollectionConflictException conflict)
        {
            return Results.Json(new { code = "stale_revision", message = conflict.Message,
                currentRevision = conflict.CurrentRevision }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> AddAsync(HttpRequest request, long clubId, AddClubGameRequest body,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        ClubCollectionService service, IBoardGameGeekClient bggClient, IOptions<BggOptions> bggOptions,
        CancellationToken cancellationToken)
    {
        if (await AdminAsync(request, authenticator, administrators, cancellationToken) is null) return Results.Forbid();
        if (!bggOptions.Value.IsAvailable)
            return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно отключён.", 503);
        var bggId = BggGameUrlParser.Parse(body.BggInput)
            ?? (long.TryParse(body.BggInput, out var parsed) && parsed > 0 ? parsed : null);
        if (bggId is null) return MiniAppEndpointSupport.Problem("validation", "Вставьте ссылку BGG или выберите игру из поиска.");
        try
        {
            var current = await service.GetAsync(clubId, cancellationToken);
            var existing = current.Collection.Games.SingleOrDefault(value => value.BggId == bggId.Value);
            if (existing is not null)
                return Results.Json(new { code = "already_exists", message = "Эта игра уже есть в коллекции клуба.", game = existing,
                    currentRevision = current.Revision }, statusCode: StatusCodes.Status409Conflict);
            var details = await bggClient.GetGameDetailsAsync(bggId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Игра не найдена в BGG.");
            var selected = body.ExpansionBggIds?.Distinct().ToHashSet() ?? [];
            if (selected.Any(id => details.Expansions.All(x => x.BggId != id)))
                throw new InvalidOperationException("BGG не связывает выбранное дополнение с этой игрой.");
            var game = details.Game;
            await service.AddOrReplaceGameAsync(clubId,
                new ClubCollectionGame(bggId.Value, game.Name, game.ThumbnailImageUrl, game.ImageUrl,
                    game.MinPlayers, game.MaxPlayers, game.BestPlayers,
                    details.Expansions.Where(x => selected.Contains(x.BggId))
                        .Select(x => new ClubCollectionExpansion(x.BggId, x.Name)).ToArray(),
                    game.Types, game.Categories, game.Description, game.YearPublished,
                    game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge, game.Type,
                    game.Subdomains, game.CategoryItems, game.Mechanics),
                body.ExpectedRevision, DateTimeOffset.UtcNow, cancellationToken);
            return Results.Ok(await service.GetAsync(clubId, cancellationToken));
        }
        catch (ClubCollectionConflictException conflict)
        {
            return Results.Json(new { code = "stale_revision", message = conflict.Message,
                currentRevision = conflict.CurrentRevision }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (HttpRequestException exception)
        {
            return MiniAppEndpointSupport.Problem("bgg_unavailable", exception.Message, 503);
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> RemoveAsync(HttpRequest request, long clubId, long bggId,
        long expectedRevision, TelegramMiniAppAuthenticator authenticator,
        IAdministratorStore administrators, ClubCollectionService service,
        CancellationToken cancellationToken)
    {
        if (await AdminAsync(request, authenticator, administrators, cancellationToken) is null) return Results.Forbid();
        try
        {
            if (!await service.RemoveGameAsync(clubId, bggId, expectedRevision,
                    DateTimeOffset.UtcNow, cancellationToken)) return Results.NotFound();
            return Results.Ok(await service.GetAsync(clubId, cancellationToken));
        }
        catch (ClubCollectionConflictException conflict)
        {
            return Results.Json(new { code = "stale_revision", message = conflict.Message,
                currentRevision = conflict.CurrentRevision }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> QueueMetadataRefreshAsync(HttpRequest request, long clubId,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        ClubMetadataRefreshService service, IOptions<BggOptions> bggOptions, CancellationToken cancellationToken)
    {
        if (await AdminAsync(request, authenticator, administrators, cancellationToken) is null) return Results.Forbid();
        if (!bggOptions.Value.IsAvailable) return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно отключён.", 503);
        try { return Results.Accepted(value: await service.QueueAsync(clubId, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> GetMetadataRefreshAsync(HttpRequest request, long clubId, Guid publicId,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        ClubMetadataRefreshService service, CancellationToken cancellationToken)
    {
        if (await AdminAsync(request, authenticator, administrators, cancellationToken) is null) return Results.Forbid();
        try { return Results.Ok(await service.GetAsync(publicId, clubId, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }
}
