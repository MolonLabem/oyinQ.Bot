using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record SavePlayPlayerRequest(Guid PlayerId, decimal? Score, bool IsWinner);
internal sealed record SavePlayRequest(string CommunityKey, bool WasPlayed, string? EndedAtLocal,
    int? DurationMinutes, IReadOnlyCollection<Guid>? PlayerIds, IReadOnlyCollection<SavePlayPlayerRequest>? PlayerResults,
    IReadOnlyCollection<long> ExpansionIds, int ExpectedRevision, bool? HigherScoreWins, string? Location);

internal static class PlayEndpoints
{
    public static RouteGroupBuilder MapPlayEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/gatherings/{id:guid}/play", GetAsync);
        group.MapPut("/gatherings/{id:guid}/play", SaveAsync);
        group.MapGet("/gatherings/{id:guid}/play/export", ExportAsync);
        group.MapGet("/profile/plays", HistoryAsync);
        group.MapPost("/gatherings/{id:guid}/play/references", AddReferenceAsync);
        group.MapDelete("/gatherings/{id:guid}/play/references/{referenceId:long}", RemoveReferenceAsync);
        return group;
    }

    private static async Task<IResult> GetAsync(HttpRequest request, Guid id, string community,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver,
        IAdminAuthorizationService authorization, AppDbContext db, GatheringPlayService service,
        CancellationToken ct)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, auth);
        if (identity is null) return Results.Unauthorized();
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, auth, resolver, ct);
        var canAdminister = await authorization.CanAdministerCommunityAsync(identity.TelegramUserId, community, ct);
        if (access is null && !canAdminister) return Results.Forbid();
        var p = await db.Participants.SingleAsync(x => x.TelegramUserId == identity.TelegramUserId, ct);
        var g = await service.Gatherings.SingleOrDefaultAsync(x => x.PublicId == id && x.CommunityKey == community, ct);
        if (g is null) return Results.NotFound();
        try
        {
            GatheringPlayService.RequireAccess(g, p.Id, canAdminister);
            var record = await db.GatheringPlayRecords.AsNoTracking().Include(x => x.Players).SingleOrDefaultAsync(x => x.GatheringId == g.Id, ct);
            if (record is not null) record.Gathering = g;
            var canShare = record is not null && ExternalPlayReferenceService.CanShare(record, p.Id);
            return Results.Ok(new
            {
                Revision = g.OutcomeRevision, WasPlayed = g.ConfirmedWasPlayed, record?.EndedAtUtc, record?.DurationMinutes,
                Location = string.IsNullOrWhiteSpace(record?.Location) ? g.Community.Name : record.Location,
                CanEdit = g.OrganizerParticipantId == p.Id || canAdminister,
                CanShare = canShare,
                References = record is null ? [] :
                    (await db.GatheringExternalPlayReferences.AsNoTracking().Include(x => x.AddedByParticipant)
                    .Where(x => x.GatheringPlayRecordId == record.Id
                        && (canShare || x.AddedByParticipantId == p.Id)).ToArrayAsync(ct))
                    .OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.Url,
                        Author = x.AddedByParticipant.PreferredDisplayName ?? x.AddedByParticipant.DisplayName,
                        CanRemove = ExternalPlayReferenceService.CanRemove(x, g.OrganizerParticipantId, p.Id) }).ToArray(),
                HigherScoreWins = record?.HigherScoreWins ?? true,
                Players = GatheringPlayService.PlayerChoices(g).Select(x =>
                {
                    var saved = record?.Players.SingleOrDefault(p => p.SourcePlayerId == x.Id);
                    return new { x.Id, x.Name, saved?.Score, IsWinner = saved?.IsWinner ?? false };
                }),
                SelectedPlayerIds = record?.Players.Select(x => x.SourcePlayerId).ToArray() ?? GatheringPlayService.SuggestedPlayerIds(g),
                Expansions = GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson).SelectedExpansions,
                SelectedExpansionIds = record is null ? null : GatheringGameSnapshotSerializer.Deserialize(record.GameSnapshotJson).SelectedExpansions.Select(x => x.BggId).ToArray()
            });
        }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }

    private static async Task<IResult> SaveAsync(HttpRequest request, Guid id, SavePlayRequest body,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, ICommunityStore communityStore,
        IAdminAuthorizationService authorization, AppDbContext db, GatheringPlayService service,
        CancellationToken ct)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, auth);
        if (identity is null) return Results.Unauthorized();
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey, auth, resolver, ct);
        var canAdminister = await authorization.CanAdministerCommunityAsync(identity.TelegramUserId, body.CommunityKey, ct);
        if (access is null && !canAdminister) return Results.Forbid();
        try
        {
            var resolvedCommunity = access?.Community
                ?? await communityStore.FindByKeyAsync(body.CommunityKey, ct)
                ?? throw new KeyNotFoundException("Сообщество не найдено.");
            var p = await db.Participants.SingleAsync(x => x.TelegramUserId == identity.TelegramUserId, ct);
            var end = body.WasPlayed ? CommunityTime.ParseLocal(body.EndedAtLocal ?? "", resolvedCommunity.TimeZoneId) : (DateTimeOffset?)null;
            var playerResults = body.PlayerResults?.Select(x => new PlayPlayerResult(x.PlayerId, x.Score, x.IsWinner)).ToArray()
                ?? body.PlayerIds?.Select(x => new PlayPlayerResult(x, null, false)).ToArray()
                ?? [];
            var result = await service.SaveAsync(id, body.CommunityKey, p.Id, new(body.WasPlayed, end,
                body.DurationMinutes, playerResults, body.ExpansionIds, body.ExpectedRevision,
                body.HigherScoreWins ?? true, body.Location), ct, canAdminister);
            return Results.Ok(new { result?.PublicId });
        }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }

    private static async Task<IResult> ExportAsync(HttpRequest request, Guid id, string community,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, GatheringPlayService service, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, auth, resolver, ct);
        if (access is null) return Results.Forbid();
        try
        {
            var p = await db.Participants.SingleAsync(x => x.TelegramUserId == access.Identity.TelegramUserId, ct);
            var g = await service.Gatherings.SingleOrDefaultAsync(x => x.PublicId == id && x.CommunityKey == community, ct)
                ?? throw new KeyNotFoundException("Сбор не найден.");
            GatheringPlayService.RequireAccess(g, p.Id);
            var record = await db.GatheringPlayRecords.Include(x => x.Players).SingleOrDefaultAsync(x => x.GatheringId == g.Id, ct)
                ?? throw new KeyNotFoundException("Сначала сохраните запись партии.");
            record.Gathering = g;
            if (!ExternalPlayReferenceService.CanShare(record, p.Id)) return Results.Forbid();
            var portable = PlayExport.From(record);
            return Results.Ok(new { BgStatsUrl = BgStatsPlayExportAdapter.Build(portable) });
        }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }

    internal sealed record ReferenceRequest(string CommunityKey, string Url);
    private static async Task<IResult> AddReferenceAsync(HttpRequest request, Guid id, ReferenceRequest body,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, ExternalPlayReferenceService service, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey, auth, resolver, ct);
        if (access is null) return Results.Forbid();
        try
        {
            var participantId = await db.Participants.Where(x => x.TelegramUserId == access.Identity.TelegramUserId).Select(x => x.Id).SingleAsync(ct);
            await service.AddAsync(id, body.CommunityKey, participantId, body.Url, ct);
            return Results.NoContent();
        }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }
    private static async Task<IResult> RemoveReferenceAsync(HttpRequest request, Guid id, long referenceId, string community,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, ExternalPlayReferenceService service, CancellationToken ct)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, auth, resolver, ct);
        if (access is null) return Results.Forbid();
        try
        {
            var participantId = await db.Participants.Where(x => x.TelegramUserId == access.Identity.TelegramUserId).Select(x => x.Id).SingleAsync(ct);
            await service.RemoveAsync(id, community, participantId, referenceId, ct);
            return Results.NoContent();
        }
        catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
    }

    private static async Task<IResult> HistoryAsync(HttpRequest request, int? page,
        TelegramMiniAppAuthenticator auth, CommunityContextResolver resolver, AppDbContext db, CancellationToken ct)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, auth);
        if (identity is null) return Results.Unauthorized();
        if (page is < 1 or > 100000) return MiniAppEndpointSupport.Problem("validation", "Неверный номер страницы.");
        var keys = (await resolver.ResolveAuthorizedAsync(identity.TelegramUserId, ct)).Select(x => x.Key).ToArray();
        var values = await db.GatheringPlayRecords.AsNoTracking().Include(x => x.Gathering).ThenInclude(x => x.Community).Include(x => x.Players)
            .Where(x => x.WasPlayed && keys.Contains(x.Gathering.CommunityKey)
                && x.Players.Any(p => p.Participant!.TelegramUserId == identity.TelegramUserId))
            .OrderByDescending(x => x.EndedAtUtc).ThenByDescending(x => x.Id).Skip(((page ?? 1) - 1) * 30).Take(31).ToArrayAsync(ct);
        return Results.Ok(new { Items = values.Take(30).Select(x => new { GatheringId = x.Gathering.PublicId,
            x.Gathering.CommunityKey, Play = PlayExport.From(x) }), HasNext = values.Length > 30 });
    }
}
