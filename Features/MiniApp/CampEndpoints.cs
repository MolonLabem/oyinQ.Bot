using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record CampRegistrationRequest(string CommunityKey, int DaysStaying,
    bool NeedsAccommodation, string? DisplayName);
internal sealed record QueueCampImportRequest(string CommunityKey, string BggInput);
internal sealed record ConfirmCampImportRequest(string CommunityKey,
    IReadOnlyCollection<long> SelectedBaseGameIds, IReadOnlyCollection<long> SelectedExpansionIds);
internal sealed record CampMutationRequest(string CommunityKey);
internal sealed record AddManualContributionRequest(string CommunityKey, string BggInput,
    IReadOnlyCollection<long>? ExpansionBggIds);
internal sealed record CampCatalogResponse(long BggId, string ItemType, long? ParentBggId,
    string Name, string? ThumbnailImageUrl, int CopyCount,
    IReadOnlyList<CampCatalogProvider> Providers, IReadOnlyList<ClubCollectionExpansion> Expansions);

internal static class CampEndpoints
{
    public static RouteGroupBuilder MapCampEndpoints(this RouteGroupBuilder group)
    {
        var camp = group.MapGroup("/camp");
        camp.MapGet("/registration", GetRegistrationAsync);
        camp.MapPut("/registration", SaveRegistrationAsync);
        camp.MapPost("/imports", QueueImportAsync);
        camp.MapGet("/imports/{publicId:guid}", GetImportAsync);
        camp.MapPost("/imports/{publicId:guid}/confirm", ConfirmImportAsync);
        camp.MapPost("/imports/{publicId:guid}/cancel", CancelImportAsync);
        camp.MapPost("/imports/{publicId:guid}/retry", RetryImportAsync);
        camp.MapGet("/contributions", GetContributionsAsync);
        camp.MapPost("/contributions/manual", AddManualAsync);
        camp.MapDelete("/contributions/{itemType}/{bggId:long}", RemoveContributionAsync);
        camp.MapGet("/catalog", GetCatalogAsync);
        return group;
    }

    private static async Task<IResult> GetRegistrationAsync(HttpRequest request, string community,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator,
            resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        if (access.Community.Mode != BotMode.Camp) return MiniAppEndpointSupport.Problem("wrong_mode", "Регистрация нужна только для кэмпа.");
        var camp = await dbContext.Camps.AsNoTracking().SingleAsync(x => x.BotChatKey == community, cancellationToken);
        var registration = await dbContext.CampRegistrations.AsNoTracking()
            .Where(x => x.CampId == camp.Id && x.Participant.TelegramUserId == access.Identity.TelegramUserId)
            .Select(x => new { Registered = true, x.DaysStaying, x.NeedsAccommodation }).SingleOrDefaultAsync(cancellationToken);
        return Results.Ok(new { CampStatus = camp.Status.ToString(), camp.StartDate, camp.EndDate,
            Registration = registration });
    }

    private static async Task<IResult> SaveRegistrationAsync(HttpRequest request, CampRegistrationRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey,
            authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        if (access.Community.Mode != BotMode.Camp) return MiniAppEndpointSupport.Problem("wrong_mode", "Регистрация нужна только для кэмпа.");
        var camp = await dbContext.Camps.SingleAsync(x => x.BotChatKey == body.CommunityKey, cancellationToken);
        if (camp.Status != CampStatus.Active) return MiniAppEndpointSupport.Problem("camp_closed", "Кэмп не принимает регистрации.");
        if (camp.StartDate is not { } start || camp.EndDate is not { } end)
            return MiniAppEndpointSupport.Problem("camp_dates_missing", "Для кэмпа не настроены даты.");
        try { CampRules.ValidateRegistrationDays(body.DaysStaying, start, end); }
        catch (ArgumentOutOfRangeException exception)
        { return MiniAppEndpointSupport.Problem("validation", exception.Message); }
        var participant = await MiniAppEndpointSupport.GetOrCreateParticipantAsync(dbContext, access.Identity,
            body.CommunityKey, cancellationToken);
        var registration = await dbContext.CampRegistrations.SingleOrDefaultAsync(
            x => x.CampId == camp.Id && x.ParticipantId == participant.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (registration is null)
        {
            registration = new CampRegistration { CampId = camp.Id, ParticipantId = participant.Id, CreatedAt = now };
            dbContext.CampRegistrations.Add(registration);
        }
        registration.DaysStaying = body.DaysStaying;
        registration.NeedsAccommodation = body.NeedsAccommodation;
        registration.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(body.DisplayName)) participant.PreferredDisplayName = body.DisplayName.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> QueueImportAsync(HttpRequest request, QueueCampImportRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CampBggImportCoordinator coordinator, IOptions<BggOptions> bggOptions,
        CancellationToken cancellationToken)
    {
        if (!bggOptions.Value.IsAvailable) return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно отключён.", 503);
        var owned = await OwnedCampAsync(request, body.CommunityKey, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        var username = BggUsernameParser.Parse(body.BggInput);
        if (username is null) return MiniAppEndpointSupport.Problem("validation", "Не удалось распознать имя пользователя BGG.");
        try
        {
            var import = await coordinator.QueueAsync(owned.CampId, owned.ParticipantId, username, cancellationToken);
            return Results.Accepted($"/api/miniapp/camp/imports/{import.PublicId}", new { import.PublicId });
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> GetImportAsync(HttpRequest request, Guid publicId, string community,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CampBggImportCoordinator coordinator, CancellationToken cancellationToken)
    {
        var owned = await OwnedCampAsync(request, community, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        try { return Results.Ok(await coordinator.GetAsync(publicId, owned.CampId, owned.ParticipantId, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> ConfirmImportAsync(HttpRequest request, Guid publicId,
        ConfirmCampImportRequest body, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CampBggImportCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedCampAsync(request, body.CommunityKey, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        try
        {
            await coordinator.ConfirmAsync(publicId, owned.CampId, owned.ParticipantId,
                body.SelectedBaseGameIds, body.SelectedExpansionIds, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static Task<IResult> CancelImportAsync(HttpRequest request, Guid publicId, CampMutationRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CampBggImportCoordinator coordinator, CancellationToken cancellationToken) =>
        MutateImportAsync(false, request, publicId, body.CommunityKey, dbContext, authenticator, resolver,
            coordinator, cancellationToken);

    private static Task<IResult> RetryImportAsync(HttpRequest request, Guid publicId, CampMutationRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CampBggImportCoordinator coordinator, CancellationToken cancellationToken) =>
        MutateImportAsync(true, request, publicId, body.CommunityKey, dbContext, authenticator, resolver,
            coordinator, cancellationToken);

    private static async Task<IResult> MutateImportAsync(bool retry, HttpRequest request, Guid publicId,
        string community, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CampBggImportCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedCampAsync(request, community, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        try
        {
            if (retry) await coordinator.RetryAsync(publicId, owned.CampId, owned.ParticipantId, cancellationToken);
            else await coordinator.CancelAsync(publicId, owned.CampId, owned.ParticipantId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> GetContributionsAsync(HttpRequest request, string community,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedCampAsync(request, community, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        var values = await dbContext.CampGameContributions.AsNoTracking()
            .Where(x => x.CampId == owned.CampId && x.ParticipantId == owned.ParticipantId)
            .OrderBy(x => x.ItemType).ThenBy(x => x.BggId).ToArrayAsync(cancellationToken);
        return Results.Ok(values.Select(x => new { x.BggId, ItemType = x.ItemType.ToString(),
            x.ParentBggId, Source = x.Source.ToString(), Snapshot = x.ReadSnapshot() }));
    }

    private static async Task<IResult> AddManualAsync(HttpRequest request, AddManualContributionRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CampContributionSelectionService contributions, IBoardGameGeekClient bggClient,
        IOptions<BggOptions> bggOptions, CancellationToken cancellationToken)
    {
        if (!bggOptions.Value.IsAvailable) return MiniAppEndpointSupport.Problem("bgg_unavailable", "BGG временно отключён.", 503);
        var owned = await OwnedCampAsync(request, body.CommunityKey, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        var bggId = BggGameUrlParser.Parse(body.BggInput)
            ?? (long.TryParse(body.BggInput, out var parsed) && parsed > 0 ? parsed : null);
        if (bggId is null) return MiniAppEndpointSupport.Problem("validation", "Вставьте ссылку BGG или выберите игру.");
        try
        {
            var details = await bggClient.GetGameDetailsAsync(bggId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Игра не найдена в BGG.");
            var selected = body.ExpansionBggIds?.Distinct().ToHashSet() ?? [];
            if (selected.Any(id => details.Expansions.All(x => x.BggId != id)))
                throw new InvalidOperationException("Выбрано неизвестное дополнение.");
            var game = details.Game;
            await contributions.AddManualAsync(owned.CampId, owned.ParticipantId, bggId.Value,
                CampContributionItemType.BaseGame, null, Snapshot(game.Name, game.ThumbnailImageUrl,
                    game.ImageUrl, game.MinPlayers, game.MaxPlayers, game.BestPlayers),
                DateTimeOffset.UtcNow, cancellationToken);
            foreach (var expansion in details.Expansions.Where(x => selected.Contains(x.BggId)))
                await contributions.AddManualAsync(owned.CampId, owned.ParticipantId, expansion.BggId,
                    CampContributionItemType.Expansion, bggId.Value,
                    Snapshot(expansion.Name, null, null, null, null, null), DateTimeOffset.UtcNow, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is not HttpRequestException)
        { return MiniAppEndpointSupport.FromException(exception); }
        catch (HttpRequestException exception)
        { return MiniAppEndpointSupport.Problem("bgg_unavailable", exception.Message, 503); }
    }

    private static async Task<IResult> RemoveContributionAsync(HttpRequest request, string itemType,
        long bggId, string community, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CampContributionSelectionService contributions,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedCampAsync(request, community, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        if (!Enum.TryParse<CampContributionItemType>(itemType, true, out var parsedType))
            return MiniAppEndpointSupport.Problem("validation", "Неизвестный тип игры.");
        try { await contributions.RemoveAsync(owned.CampId, owned.ParticipantId, bggId, parsedType, cancellationToken); return Results.NoContent(); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> GetCatalogAsync(HttpRequest request, string community,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CampContributionSelectionService contributions, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        if (access.Community.Mode != BotMode.Camp) return MiniAppEndpointSupport.Problem("wrong_mode", "Каталог кэмпа недоступен.");
        var camp = await dbContext.Camps.AsNoTracking().SingleAsync(x => x.BotChatKey == community, cancellationToken);
        var contributed = await contributions.GetEffectiveContributionsAsync(camp.Id, cancellationToken);
        var collection = camp.ReadBaseCollection();
        IReadOnlyList<ClubCollectionExpansion> ExpansionsFor(long baseId,
            IReadOnlyList<ClubCollectionExpansion> snapshot) => snapshot.Concat(contributed
                .Where(x => x.ItemType == CampContributionItemType.Expansion && x.ParentBggId == baseId)
                .Select(x => new ClubCollectionExpansion(x.BggId, x.Name)))
            .DistinctBy(x => x.BggId).OrderBy(x => x.Name).ToArray();
        var baseGames = collection.Games.Select(game => new CampCatalogResponse(
            game.BggId, CampContributionItemType.BaseGame.ToString(), null, game.Name,
            game.ThumbnailImageUrl,
            1 + contributed.Where(x => x.BggId == game.BggId && x.ItemType == CampContributionItemType.BaseGame).Sum(x => x.CopyCount),
            [new CampCatalogProvider(null, "Клуб", null), .. contributed.Where(x => x.BggId == game.BggId
                && x.ItemType == CampContributionItemType.BaseGame).SelectMany(x => x.Providers)],
            ExpansionsFor(game.BggId, game.Expansions)));
        var extra = contributed.Where(x => x.ItemType == CampContributionItemType.BaseGame
            && collection.Games.All(game => game.BggId != x.BggId))
            .Select(x => new CampCatalogResponse(x.BggId, x.ItemType.ToString(), x.ParentBggId,
                x.Name, null, x.CopyCount, x.Providers, ExpansionsFor(x.BggId, [])));
        return Results.Ok(baseGames.Concat(extra).OrderBy(x => x.Name));
    }

    private static CampContributionSnapshot Snapshot(string name, string? thumbnail, string? image,
        int? min, int? max, string? best) => new(CampContributionSnapshot.CurrentVersion,
        name, thumbnail, image, min, max, best);

    private static async Task<(long CampId, long ParticipantId, IResult? Error)> OwnedCampAsync(
        HttpRequest request, string community, AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, cancellationToken);
        if (access is null) return (0, 0, Results.Forbid());
        if (access.Community.Mode != BotMode.Camp)
            return (0, 0, MiniAppEndpointSupport.Problem("wrong_mode", "Действие доступно только в кэмпе."));
        var participant = await MiniAppEndpointSupport.GetOrCreateParticipantAsync(dbContext, access.Identity,
            community, cancellationToken);
        var campId = await dbContext.Camps.Where(x => x.BotChatKey == community).Select(x => x.Id)
            .SingleAsync(cancellationToken);
        return (campId, participant.Id, null);
    }
}
