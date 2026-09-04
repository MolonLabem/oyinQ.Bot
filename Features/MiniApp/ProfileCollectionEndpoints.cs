using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record ProfileImportRequest(string BggInput);
internal sealed record ProfileManualRequest(string BggInput, IReadOnlyCollection<long>? ExpansionBggIds);
internal sealed record ProfileImportSelection(IReadOnlyCollection<long> SelectedBaseGameIds,
    IReadOnlyCollection<long> SelectedExpansionIds);

internal static class ProfileCollectionEndpoints
{
    public static void MapProfileCollectionEndpoints(this RouteGroupBuilder group)
    {
        var profile = group.MapGroup("/profile/collection");
        profile.MapGet("/", ListAsync);
        profile.MapPost("/manual", AddAsync);
        profile.MapDelete("/{itemType}/{bggId:long}", RemoveAsync);
        profile.MapPost("/imports", QueueAsync);
        profile.MapGet("/imports/{publicId:guid}", GetImportAsync);
        profile.MapPost("/imports/{publicId:guid}/confirm", ConfirmAsync);
        profile.MapPost("/imports/{publicId:guid}/{action}", ImportActionAsync);
    }

    private static Task<long> OwnerAsync(HttpRequest request, TelegramMiniAppAuthenticator authenticator,
        AppDbContext db, CancellationToken ct)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator)
            ?? throw new UnauthorizedAccessException("Откройте приложение через Telegram.");
        return db.Participants.Where(x => x.TelegramUserId == identity.TelegramUserId)
            .Select(x => x.Id).SingleAsync(ct);
    }

    private static async Task<IResult> ListAsync(HttpRequest request, TelegramMiniAppAuthenticator authenticator,
        AppDbContext db, CancellationToken ct)
    {
        var owner = await OwnerAsync(request, authenticator, db, ct);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        var rows = await db.ParticipantCollectionItems.AsNoTracking().Where(x => x.ParticipantId == owner).ToArrayAsync(ct);
        return Results.Ok(rows.Select(x => new { x.BggId, x.ItemType, x.ParentBggId, x.Source, Snapshot = x.ReadSnapshot() }));
    }

    private static async Task<IResult> AddAsync(HttpRequest request, ProfileManualRequest body,
        TelegramMiniAppAuthenticator authenticator, AppDbContext db, IBoardGameGeekClient bgg,
        ParticipantCollectionService collection, ILogger<ParticipantCollectionService> logger, CancellationToken ct)
    {
        var owner = await OwnerAsync(request, authenticator, db, ct);
        var id = BggGameUrlParser.Parse(body.BggInput)
            ?? (long.TryParse(body.BggInput, out var parsed) && parsed > 0 ? parsed : null);
        if (id is null) return MiniAppEndpointSupport.Problem("validation", "Укажите BGG ID или ссылку.");
        try
        {
            var selected = (body.ExpansionBggIds ?? []).ToHashSet();
            var items = await bgg.GetItemsByIdsAsync(selected.Append(id.Value).ToHashSet(), ct);
            if (!items.Any(x => x.Game.BggId == id)
                || selected.Any(expansionId => !items.Any(x => x.Game.BggId == expansionId
                    && x.IsExpansion && x.ParentBggIds.Contains(id.Value))))
                throw new InvalidOperationException("BGG не подтвердил игру или связь дополнения.");
            var draft = items.Select(x => new CampBggImportDraftItem(x.Game.BggId!.Value,
                x.IsExpansion ? CollectionItemType.Expansion : CollectionItemType.BaseGame,
                x.ParentBggIds.Count > 0 ? x.ParentBggIds[0] : null,
                BggGameMapper.ToCollectionSnapshot(x.Game, x.ParentBggIds), ParentBggIds: x.ParentBggIds)).ToArray();
            await collection.UpsertAsync(owner, draft, CollectionItemSource.Manual, DateTimeOffset.UtcNow, ct);
            return Results.NoContent();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "BGG personal collection lookup failed for {BggId}", id);
            return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно недоступен. Попробуйте позже.", 503);
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> RemoveAsync(HttpRequest request, string itemType, long bggId,
        TelegramMiniAppAuthenticator authenticator, AppDbContext db, ParticipantCollectionService collection, CancellationToken ct)
    {
        if (!Enum.TryParse<CollectionItemType>(itemType, out var type) || !Enum.IsDefined(type))
            return MiniAppEndpointSupport.Problem("validation", "Неизвестный тип игры.");
        try { await collection.RemoveAsync(await OwnerAsync(request, authenticator, db, ct), bggId, type, ct); return Results.NoContent(); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> QueueAsync(HttpRequest request, ProfileImportRequest body,
        TelegramMiniAppAuthenticator authenticator, AppDbContext db, CampBggImportCoordinator coordinator, IOptions<BggOptions> options, CancellationToken ct)
    {
        if (!options.Value.IsAvailable) return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно недоступен. Сохранённая коллекция доступна.", 503);
        var username = BggUsernameParser.Parse(body.BggInput);
        if (username is null) return MiniAppEndpointSupport.Problem("validation", "Не удалось распознать имя пользователя BGG.");
        try
        {
            var job = await coordinator.QueueAsync(null, await OwnerAsync(request, authenticator, db, ct), username, ct);
            return Results.Accepted($"/api/miniapp/profile/collection/imports/{job.PublicId}", new { job.PublicId });
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> GetImportAsync(HttpRequest request, Guid publicId,
        TelegramMiniAppAuthenticator authenticator, AppDbContext db, CampBggImportCoordinator coordinator, CancellationToken ct)
    {
        try { return Results.Ok(await coordinator.GetAsync(publicId, null, await OwnerAsync(request, authenticator, db, ct), ct)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> ConfirmAsync(HttpRequest request, Guid publicId, ProfileImportSelection body,
        TelegramMiniAppAuthenticator authenticator, AppDbContext db, CampBggImportCoordinator coordinator, CancellationToken ct)
    {
        try { return Results.Ok(await coordinator.ConfirmAsync(publicId, null, await OwnerAsync(request, authenticator, db, ct),
            body.SelectedBaseGameIds, body.SelectedExpansionIds, ct)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> ImportActionAsync(HttpRequest request, Guid publicId, string action,
        TelegramMiniAppAuthenticator authenticator, AppDbContext db, CampBggImportCoordinator coordinator, CancellationToken ct)
    {
        var owner = await OwnerAsync(request, authenticator, db, ct);
        try
        {
            if (action == "retry") await coordinator.RetryAsync(publicId, null, owner, ct);
            else if (action == "cancel") await coordinator.CancelAsync(publicId, null, owner, ct);
            else return Results.NotFound();
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }
}
