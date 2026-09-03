using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record CreateGatheringRequest(string CommunityKey, string GameSource, long BggId,
    IReadOnlyCollection<long>? SelectedExpansionIds, string StartsAtLocal,
    int MinimumPlayers, int DesiredPlayers, int MaximumPlayers, string? Description, bool CanTeachRules);
internal sealed record UpdateGatheringRequest(string CommunityKey, string StartsAtLocal,
    int MinimumPlayers, int DesiredPlayers, int MaximumPlayers, string? Description,
    bool CanTeachRules, IReadOnlyCollection<long>? SelectedExpansionIds);
internal sealed record GatheringActionRequest(string CommunityKey, string? Reason = null);
internal sealed record GatheringGuestRequest(string CommunityKey, string? DisplayName = null);
internal sealed record GatheringListItemResponse(
    GatheringCardPresentation Card,
    bool IsOrganizer);
internal sealed record GatheringListPageResponse(
    IReadOnlyCollection<GatheringListItemResponse> Items,
    int Page,
    bool HasPrevious,
    bool HasNext);

internal static class GatheringEndpoints
{
    public static RouteGroupBuilder MapGatheringEndpoints(this RouteGroupBuilder group)
    {
        var gatherings = group.MapGroup("/gatherings");
        gatherings.MapGet("/", ListAsync);
        gatherings.MapGet("/{publicId:guid}", DetailAsync);
        gatherings.MapPost("/", CreateAsync);
        gatherings.MapPut("/{publicId:guid}", UpdateAsync);
        gatherings.MapPost("/{publicId:guid}/join", JoinAsync);
        gatherings.MapPost("/{publicId:guid}/leave", LeaveAsync);
        gatherings.MapPost("/{publicId:guid}/guests", AddGuestAsync);
        gatherings.MapPut("/{publicId:guid}/guests/{guestId:long}", RenameGuestAsync);
        gatherings.MapDelete("/{publicId:guid}/guests/{guestId:long}", RemoveGuestAsync);
        gatherings.MapPost("/{publicId:guid}/{action}", LifecycleAsync);
        gatherings.MapPost("/{publicId:guid}/publication/retry", RetryPublicationAsync);
        return group;
    }

    private const int GatheringPageSize = 20;

    private static async Task<IResult> ListAsync(HttpRequest request, string community, string? view, string? status, int? page,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, GatheringPresentationService presentation,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        if (!GatheringListQuery.TryParse(view, status, out var parsedView, out var parsedFilter))
            return MiniAppEndpointSupport.Problem("invalid_gathering_view",
                "Поддерживаются view=upcoming или view=history; фильтры истории: completed, cancelled.", 400);
        var pageNumber = page ?? 1;
        if (pageNumber < 1 || pageNumber > int.MaxValue / GatheringPageSize)
            return MiniAppEndpointSupport.Problem("invalid_gathering_page", "Номер страницы должен быть больше нуля.", 400);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        var query = dbContext.GameGatherings.AsNoTracking().Where(x => x.CommunityKey == community)
            .Include(x => x.Participants).Include(x => x.Guests).Include(x => x.OrganizerParticipant);
        var values = await GatheringListQuery.Apply(query, parsedView, parsedFilter, timeProvider.GetUtcNow())
            .Skip((pageNumber - 1) * GatheringPageSize)
            .Take(GatheringPageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasNext = values.Length > GatheringPageSize;
        var items = values.Take(GatheringPageSize).Select(x => new GatheringListItemResponse(
            presentation.BuildCard(x, access.Community),
            x.OrganizerParticipant.TelegramUserId == access.Identity.TelegramUserId)).ToArray();
        return Results.Ok(new GatheringListPageResponse(items, pageNumber, pageNumber > 1, hasNext));
    }

    private static async Task<IResult> DetailAsync(HttpRequest request, Guid publicId, string community,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, GatheringPresentationService presentation,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        var gathering = await dbContext.GameGatherings.AsNoTracking()
            .Include(x => x.OrganizerParticipant).Include(x => x.Expansions)
            .Include(x => x.Participants).ThenInclude(x => x.Participant)
            .Include(x => x.Guests)
            .SingleOrDefaultAsync(x => x.PublicId == publicId && x.CommunityKey == community, cancellationToken);
        if (gathering is null) return Results.NotFound();
        var me = gathering.Participants.SingleOrDefault(x => x.Participant.TelegramUserId == access.Identity.TelegramUserId);
        var manages = gathering.OrganizerParticipant.TelegramUserId == access.Identity.TelegramUserId;
        var active = me?.Status is GatheringParticipationStatus.Confirmed or GatheringParticipationStatus.Waitlisted;
        var now = timeProvider.GetUtcNow();
        var waitlisted = gathering.Participants.Where(x => x.Status == GatheringParticipationStatus.Waitlisted)
            .OrderBy(x => x.JoinedAt).ThenBy(x => x.Id).ToArray();
        var waitPosition = me?.Status == GatheringParticipationStatus.Waitlisted
            ? Array.IndexOf(waitlisted, me) + 1 : (int?)null;
        var snapshot = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        var localStart = TimeZoneInfo.ConvertTime(gathering.StartsAtUtc,
            TimeZoneInfo.FindSystemTimeZoneById(access.Community.TimeZoneId));
        return Results.Ok(new
        {
            Gathering = presentation.BuildDetails(gathering, access.Community),
            Status = gathering.Status.ToString(),
            CurrentUserStatus = manages ? "Organizer" : me?.Status.ToString() ?? "None",
            CanManage = manages,
            CanEdit = manages && gathering.StartsAtUtc > now
                && gathering.Status is not GatheringStatus.Completed and not GatheringStatus.Cancelled,
            HasStarted = gathering.StartsAtUtc <= now,
            CanJoin = GatheringAccessPolicy.CanJoin(gathering, manages, active, now),
            CanLeave = GatheringAccessPolicy.CanLeave(gathering, manages, active, now),
            WaitlistPosition = waitPosition,
            ConfirmedParticipants = new[] { new { Name = ParticipantPresentation.GetDisplayName(gathering.OrganizerParticipant), IsOrganizer = true,
                    ContactUrl = ParticipantPresentation.GetContactUrl(gathering.OrganizerParticipant) } }
                .Concat(gathering.Participants.Where(x => x.Status == GatheringParticipationStatus.Confirmed)
                    .Select(x => new { Name = ParticipantPresentation.GetDisplayName(x.Participant),
                        IsOrganizer = false, ContactUrl = ParticipantPresentation.GetContactUrl(x.Participant) })),
            WaitlistedParticipants = waitlisted.Select((x, index) => new
            { Name = ParticipantPresentation.GetDisplayName(x.Participant), Position = index + 1,
                ContactUrl = ParticipantPresentation.GetContactUrl(x.Participant) }),
            GuestParticipants = gathering.Guests.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.DisplayName }),
            PublicationStatus = gathering.PublicationStatus.ToString(),
            gathering.PublicationError,
            CanRetryPublication = manages && gathering.PublicationStatus == GatheringPublicationStatus.Failed
            ,StartsAtLocal = localStart.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)
            ,gathering.MinimumPlayers
            ,gathering.DesiredPlayers
            ,gathering.MaximumPlayers
            ,GameMinimumPlayers = snapshot.MinPlayers
            ,GameMaximumPlayers = snapshot.MaxPlayers
            ,GamePlayerRangeDefaulted = snapshot.PlayerRangeDefaulted
            ,gathering.Description
            ,gathering.CanTeachRules
            ,KnownExpansions = (snapshot.KnownExpansions ?? snapshot.SelectedExpansions)
            ,SelectedExpansionIds = snapshot.SelectedExpansions.Select(x => x.BggId)
        });
    }

    private static async Task<IResult> CreateAsync(HttpRequest request, CreateGatheringRequest body,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GatheringManagementService management, GatheringPublicationService publication,
        ILogger<BoardGameGeekClient> logger, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey,
            authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        try
        {
            var startsAt = ParseLocal(body.StartsAtLocal, access.Community.TimeZoneId);
            var gathering = await management.CreateAsync(access.Community, access.Identity,
                new(body.CommunityKey, body.GameSource, body.BggId, body.SelectedExpansionIds ?? [], startsAt,
                    body.MinimumPlayers, body.DesiredPlayers, body.MaximumPlayers,
                    body.Description, body.CanTeachRules), cancellationToken);
            var published = await publication.PublishAsync(gathering.PublicId, cancellationToken);
            return Results.Created($"/api/miniapp/gatherings/{gathering.PublicId}",
                new { gathering.PublicId, AnnouncementPublished = published });
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "BGG failed while creating gathering from external game {BggId}.", body.BggId);
            return MiniAppEndpointSupport.Problem("bgg_unavailable",
                "BGG временно недоступен. Выберите сохранённую игру из каталога или попробуйте позже.", 503);
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> UpdateAsync(HttpRequest request, Guid publicId,
        UpdateGatheringRequest body, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, GatheringManagementService management,
        GatheringPublicationService publication, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey,
            authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        try
        {
            var startsAt = ParseLocal(body.StartsAtLocal, access.Community.TimeZoneId);
            await management.UpdateAsync(publicId, body.CommunityKey, access.Identity.TelegramUserId,
                new(startsAt, body.MinimumPlayers, body.DesiredPlayers, body.MaximumPlayers,
                    body.Description, body.CanTeachRules, body.SelectedExpansionIds ?? []), cancellationToken);
            await publication.PublishAsync(publicId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static Task<IResult> JoinAsync(HttpRequest request, Guid publicId, GatheringActionRequest body,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GatheringService service, GatheringPublicationService publication,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) => MutateParticipationAsync(true, request, publicId, body,
            authenticator, resolver, service, publication, timeProvider, cancellationToken);

    private static Task<IResult> LeaveAsync(HttpRequest request, Guid publicId, GatheringActionRequest body,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GatheringService service, GatheringPublicationService publication,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) => MutateParticipationAsync(false, request, publicId, body,
            authenticator, resolver, service, publication, timeProvider, cancellationToken);

    private static Task<IResult> AddGuestAsync(HttpRequest request, Guid publicId, GatheringGuestRequest body,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GatheringService service, GatheringPublicationService publication, TimeProvider timeProvider,
        CancellationToken cancellationToken) => MutateGuestAsync("add", request, publicId, null, body,
            authenticator, resolver, service, publication, timeProvider, cancellationToken);

    private static Task<IResult> RenameGuestAsync(HttpRequest request, Guid publicId, long guestId,
        GatheringGuestRequest body, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GatheringService service, GatheringPublicationService publication, TimeProvider timeProvider,
        CancellationToken cancellationToken) => MutateGuestAsync("rename", request, publicId, guestId, body,
            authenticator, resolver, service, publication, timeProvider, cancellationToken);

    private static Task<IResult> RemoveGuestAsync(HttpRequest request, Guid publicId, long guestId,
        [FromBody] GatheringGuestRequest body, TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        GatheringService service, GatheringPublicationService publication, TimeProvider timeProvider,
        CancellationToken cancellationToken) => MutateGuestAsync("remove", request, publicId, guestId, body,
            authenticator, resolver, service, publication, timeProvider, cancellationToken);

    private static async Task<IResult> MutateGuestAsync(string action, HttpRequest request, Guid publicId,
        long? guestId, GatheringGuestRequest body, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, GatheringService service, GatheringPublicationService publication,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey,
            authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        try
        {
            var now = timeProvider.GetUtcNow();
            var result = action switch
            {
                "add" => await service.AddGuestAsync(publicId, body.CommunityKey,
                    access.Identity.TelegramUserId, body.DisplayName, now, cancellationToken),
                "rename" => await service.RenameGuestAsync(publicId, guestId!.Value, body.CommunityKey,
                    access.Identity.TelegramUserId, body.DisplayName, now, cancellationToken),
                "remove" => await service.RemoveGuestAsync(publicId, guestId!.Value, body.CommunityKey,
                    access.Identity.TelegramUserId, now, cancellationToken),
                _ => throw new InvalidOperationException("Неизвестное действие с гостем.")
            };
            if (result.Promotion is not null)
                await publication.NotifyPromotionAsync(result.Promotion, publicId, cancellationToken);
            await publication.PublishAsync(publicId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> MutateParticipationAsync(bool join, HttpRequest request, Guid publicId,
        GatheringActionRequest body, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, GatheringService service,
        GatheringPublicationService publication, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey,
            authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        try
        {
            var result = join
                ? await service.JoinAsync(publicId, body.CommunityKey, access.Identity.TelegramUserId,
                    timeProvider.GetUtcNow(), cancellationToken)
                : await service.LeaveAsync(publicId, body.CommunityKey, access.Identity.TelegramUserId,
                    timeProvider.GetUtcNow(), cancellationToken);
            if (result.Promotion is not null)
                await publication.NotifyPromotionAsync(result.Promotion, publicId, cancellationToken);
            await publication.PublishAsync(publicId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> LifecycleAsync(HttpRequest request, Guid publicId, string action,
        GatheringActionRequest body, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, GatheringManagementService management,
        GatheringPublicationService publication, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey,
            authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        try
        {
            await management.ChangeLifecycleAsync(publicId, body.CommunityKey,
                access.Identity.TelegramUserId, action, body.Reason, cancellationToken);
            await publication.PublishAsync(publicId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> RetryPublicationAsync(HttpRequest request, Guid publicId,
        GatheringActionRequest body, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, GatheringPublicationService publication,
        CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey,
            authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        var manages = await dbContext.GameGatherings.AsNoTracking().AnyAsync(x => x.PublicId == publicId
            && x.CommunityKey == body.CommunityKey
            && x.OrganizerParticipant.TelegramUserId == access.Identity.TelegramUserId, cancellationToken);
        if (!manages) return Results.Forbid();
        return Results.Ok(new { Published = await publication.PublishAsync(publicId, cancellationToken) });
    }

    private static DateTimeOffset ParseLocal(string value, string timeZoneId)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
            throw new ArgumentException("Укажите корректные дату и время.");
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ArgumentException("Выбранное локальное время неоднозначно.");
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
