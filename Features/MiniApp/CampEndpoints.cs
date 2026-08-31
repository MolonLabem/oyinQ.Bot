using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record CampRegistrationRequest(string CommunityKey, IReadOnlyCollection<DateOnly> SelectedDates,
    bool NeedsAccommodation, string? DisplayName, string City, bool ConfirmAttendanceChanges = false);
internal sealed record QueueCampImportRequest(string CommunityKey, string BggInput);
internal sealed record ConfirmCampImportRequest(string CommunityKey,
    IReadOnlyCollection<long> SelectedBaseGameIds, IReadOnlyCollection<long> SelectedExpansionIds);
internal sealed record CampMutationRequest(string CommunityKey);
internal sealed record CampCommitmentRequest(string CommunityKey, CampBringCommitment Commitment);
internal sealed record ResolveCampImportRequest(string CommunityKey, CampImportOverrideResolution Resolution);
internal sealed record AddManualContributionRequest(string CommunityKey, string BggInput,
    IReadOnlyCollection<long>? ExpansionBggIds);
internal sealed record CampCatalogResponse(long BggId, string ItemType, long? ParentBggId,
    string Name, string? ThumbnailImageUrl, string? ImageUrl, int? MinPlayers, int? MaxPlayers,
    string? BestPlayers, GameType Type, string TypeName, IReadOnlyList<string> TypeNames,
    IReadOnlyList<string> CategoryNames,
    IReadOnlyList<string> MechanicNames, int CopyCount,
    IReadOnlyList<CampCatalogProvider> Providers, IReadOnlyList<ClubCollectionExpansion> Expansions,
    bool IsInBaseCollection, bool HasCommittedProvider, bool NeedsProviderCoordination);

internal static class CampEndpoints
{
    public static RouteGroupBuilder MapCampEndpoints(this RouteGroupBuilder group)
    {
        var camp = group.MapGroup("/camp");
        camp.MapGet("/registration", GetRegistrationAsync);
        camp.MapPut("/registration", SaveRegistrationAsync);
        camp.MapPost("/registration/unregister", UnregisterAsync);
        camp.MapPost("/imports", QueueImportAsync);
        camp.MapGet("/imports/{publicId:guid}", GetImportAsync);
        camp.MapPost("/imports/{publicId:guid}/confirm", ConfirmImportAsync);
        camp.MapPost("/imports/{publicId:guid}/cancel", CancelImportAsync);
        camp.MapPost("/imports/{publicId:guid}/retry", RetryImportAsync);
        camp.MapPost("/imports/{publicId:guid}/resolve-base-duplicates", ResolveBaseDuplicatesAsync);
        camp.MapGet("/contributions", GetContributionsAsync);
        camp.MapPost("/contributions/manual", AddManualAsync);
        camp.MapDelete("/contributions/{itemType}/{bggId:long}", RemoveContributionAsync);
        camp.MapPut("/contributions/{itemType}/{bggId:long}/commitment", SetCommitmentAsync);
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
        var registration = await dbContext.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays)
            .Where(x => x.CampId == camp.Id && x.Participant.TelegramUserId == access.Identity.TelegramUserId)
            .Select(x => new { Row = x, x.DisplayName }).SingleOrDefaultAsync(cancellationToken);
        var storedParticipantDisplayName = await dbContext.Participants.AsNoTracking()
            .Where(x => x.TelegramUserId == access.Identity.TelegramUserId)
            .Select(x => x.PreferredDisplayName ?? x.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);
        var participantDisplayName = CampParticipantPresentation.RegistrationDisplayName(registration?.DisplayName,
            storedParticipantDisplayName, access.Identity.DisplayName);
        var availableDates = camp.StartDate is { } start && camp.EndDate is { } end
            ? Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(start.AddDays).ToArray() : [];
        var selectedDates = registration?.Row.SelectedDays.Select(x => x.Date).Order().ToArray() ?? [];
        var suggestedDates = registration is null ? availableDates
            : selectedDates.Length == 0 && registration.Row.DaysStaying == availableDates.Length
                ? availableDates : [];
        var baseGameIds = camp.ReadBaseCollection().Games.Select(x => x.BggId).Order().ToArray();
        return Results.Ok(new { CampStatus = camp.Status.ToString(), camp.StartDate, camp.EndDate,
            AvailableDates = availableDates, BaseGameIds = baseGameIds, DisplayName = participantDisplayName,
            Registration = registration is null ? null : new
            {
                Registered = CampParticipationPolicy.IsRegistrationComplete(registration.Row, camp),
                registration.Row.DaysStaying,
                registration.Row.NeedsAccommodation,
                registration.Row.City,
                registration.DisplayName,
                SelectedDates = selectedDates,
                SuggestedDates = suggestedDates
            } });
    }

    private static async Task<IResult> SaveRegistrationAsync(HttpRequest request, CampRegistrationRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CampRegistrationService registrations,
        GatheringPublicationService publication, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey,
            authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        if (access.Community.Mode != BotMode.Camp) return MiniAppEndpointSupport.Problem("wrong_mode", "Регистрация нужна только для кэмпа.");
        var camp = await dbContext.Camps.SingleAsync(x => x.BotChatKey == body.CommunityKey, cancellationToken);
        if (camp.Status != CampStatus.Active) return MiniAppEndpointSupport.Problem("camp_closed", "Кэмп не принимает регистрации.");
        if (CampParticipationPolicy.HasEnded(camp, access.Community.TimeZoneId, timeProvider.GetUtcNow()))
            return MiniAppEndpointSupport.Problem("camp_ended", "Кэмп уже завершён и не принимает изменения.");
        if (camp.StartDate is null || camp.EndDate is null)
            return MiniAppEndpointSupport.Problem("camp_dates_missing",
                "Организатор ещё не указал даты кэмпа. Откройте настройки кэмпа в админ-панели.");
        var participant = await MiniAppEndpointSupport.GetOrCreateParticipantAsync(dbContext, access.Identity,
            body.CommunityKey, cancellationToken);
        try
        {
            var result = await registrations.SaveAsync(camp.Id, participant.Id, body.SelectedDates,
                body.NeedsAccommodation, body.DisplayName, body.City, body.ConfirmAttendanceChanges,
                cancellationToken);
            await PublishRegistrationChangesAsync(result, publication, cancellationToken);
            return Results.NoContent();
        }
        catch (CampRegistrationConflictException exception)
        {
            return Results.Json(new { Code = exception.CanConfirm ? "registration_dates_affect_gatherings" : "registration_organizer_conflict", exception.Message,
                AffectedGatherings = exception.Gatherings }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> UnregisterAsync(HttpRequest request, CampMutationRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CampRegistrationService registrations, GatheringPublicationService publication,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedCampAsync(request, body.CommunityKey, dbContext, authenticator, resolver,
            cancellationToken);
        if (owned.Error is not null) return owned.Error;
        try
        {
            var result = await registrations.UnregisterAsync(owned.CampId, owned.ParticipantId,
                cancellationToken);
            await PublishRegistrationChangesAsync(result, publication, cancellationToken);
            return Results.NoContent();
        }
        catch (CampRegistrationConflictException exception)
        {
            return Results.Json(new { Code = "registration_organizer_conflict", exception.Message,
                AffectedGatherings = exception.Gatherings }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task PublishRegistrationChangesAsync(CampRegistrationMutationResult result,
        GatheringPublicationService publication, CancellationToken cancellationToken)
    {
        foreach (var gatheringId in result.ChangedGatheringIds)
            await publication.PublishAsync(gatheringId, cancellationToken);
        foreach (var promotion in result.Promotions)
            await publication.NotifyPromotionAsync(promotion.Promotion, promotion.GatheringPublicId,
                cancellationToken);
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
        CampImportNotificationService notifications,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedCampAsync(request, body.CommunityKey, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        try
        {
            var result = await coordinator.ConfirmAsync(publicId, owned.CampId, owned.ParticipantId,
                body.SelectedBaseGameIds, body.SelectedExpansionIds, cancellationToken);
            var telegramUserId = await dbContext.Participants.Where(x => x.Id == owned.ParticipantId)
                .Select(x => x.TelegramUserId).SingleAsync(cancellationToken);
            await notifications.NotifyAsync(telegramUserId, body.CommunityKey, publicId, result, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static Task<IResult> CancelImportAsync(HttpRequest request, Guid publicId, CampMutationRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CampBggImportCoordinator coordinator, CancellationToken cancellationToken) =>
        MutateImportAsync(false, request, publicId, body.CommunityKey, dbContext, authenticator, resolver,
            coordinator, cancellationToken);

    private static async Task<IResult> ResolveBaseDuplicatesAsync(HttpRequest request, Guid publicId,
        ResolveCampImportRequest body, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CampBggImportCoordinator coordinator, CancellationToken cancellationToken)
    {
        var owned = await OwnedCampAsync(request, body.CommunityKey, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        try { await coordinator.ResolveBaseDuplicatesAsync(publicId, owned.CampId, owned.ParticipantId,
            body.Resolution, cancellationToken); return Results.NoContent(); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

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
            x.ParentBggId, Source = x.Source.ToString(), Commitment = x.Commitment.ToString(), Snapshot = x.ReadSnapshot() }));
    }

    private static async Task<IResult> AddManualAsync(HttpRequest request, AddManualContributionRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CampContributionSelectionService contributions, IBoardGameGeekClient bggClient,
        IOptions<BggOptions> bggOptions, ILogger<BoardGameGeekClient> logger,
        CancellationToken cancellationToken)
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
                    game.ImageUrl, game.MinPlayers, game.MaxPlayers, game.BestPlayers,
                    game.Types, game.Categories, game),
                DateTimeOffset.UtcNow, cancellationToken);
            foreach (var expansion in details.Expansions.Where(x => selected.Contains(x.BggId)))
                await contributions.AddManualAsync(owned.CampId, owned.ParticipantId, expansion.BggId,
                    CampContributionItemType.Expansion, bggId.Value,
                    Snapshot(expansion.Name, null, null, null, null, null, null, null, null) with
                    { ParentBggIds = [bggId.Value] },
                    DateTimeOffset.UtcNow, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is not HttpRequestException)
        { return MiniAppEndpointSupport.FromException(exception); }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "BGG failed while adding Camp contribution {BggId}.", bggId);
            return MiniAppEndpointSupport.Problem("bgg_unavailable",
                "Не удалось загрузить данные BGG. Ваши сохранённые игры не изменены.", 503);
        }
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

    private static async Task<IResult> SetCommitmentAsync(HttpRequest request, string itemType, long bggId,
        CampCommitmentRequest body, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CampContributionSelectionService contributions,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedCampAsync(request, body.CommunityKey, dbContext, authenticator, resolver, cancellationToken);
        if (owned.Error is not null) return owned.Error;
        if (!Enum.TryParse<CampContributionItemType>(itemType, true, out var parsedType))
            return MiniAppEndpointSupport.Problem("validation", "Неизвестный тип игры.");
        try
        {
            await contributions.SetCommitmentAsync(owned.CampId, owned.ParticipantId, bggId, parsedType,
                body.Commitment, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> GetCatalogAsync(HttpRequest request, string community,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        EffectiveCampCatalogService catalog, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        if (access.Community.Mode != BotMode.Camp) return MiniAppEndpointSupport.Problem("wrong_mode", "Каталог кэмпа недоступен.");
        var participantId = await dbContext.Participants.Where(x => x.TelegramUserId == access.Identity.TelegramUserId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var effective = await catalog.LoadAsync(community, participantId, cancellationToken);
        return Results.Ok(effective.Select(x => { var metadata = BggTaxonomyCatalog.Present(x.Game); return new CampCatalogResponse(
            x.Game.BggId, CampContributionItemType.BaseGame.ToString(), null, x.Game.Name,
            x.Game.ThumbnailImageUrl, x.Game.ImageUrl, x.Game.MinPlayers, x.Game.MaxPlayers,
            x.Game.BestPlayers, x.Game.Type, metadata.TypeName, metadata.TypeNames,
            metadata.CategoryNames, metadata.MechanicNames,
            (x.IsInBaseCollection ? 1 : 0) + x.Providers.Count, x.Providers,
            x.Game.Expansions, x.IsInBaseCollection,
            x.Providers.Any(p => p.Commitment == CampBringCommitment.Bringing),
            !x.IsInBaseCollection && x.Providers.Count > 1
                && x.Providers.All(p => p.Commitment != CampBringCommitment.Bringing)); }));
    }

    private static CampContributionSnapshot Snapshot(string name, string? thumbnail, string? image,
        int? min, int? max, string? best, IReadOnlyList<string>? types,
        IReadOnlyList<string>? categories, oyinQ.Bot.Integrations.ExternalGame? game) => new(CampContributionSnapshot.CurrentVersion,
        name, thumbnail, image, min, max, best, types, categories,
        game?.Description, game?.YearPublished, game?.MinPlayTimeMinutes, game?.MaxPlayTimeMinutes,
        game?.MinAge, game?.Type ?? GameType.Other, game?.Subdomains, game?.CategoryItems, game?.Mechanics);

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
