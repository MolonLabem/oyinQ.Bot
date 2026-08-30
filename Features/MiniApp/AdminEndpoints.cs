using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record CreatePeerSelectionRequest(string Purpose);
internal sealed record PeerSelectionTokenRequest(Guid SelectionId);
internal sealed record CreateClubRequest(Guid SelectionId, string? Name, string TimeZoneId);
internal sealed record CreateCampRequest(Guid SelectionId, string? Name, DateOnly StartDate, DateOnly EndDate,
    long? SourceClubId, string? TimeZoneId);
internal sealed record UpdateCommunityRequest(string Name, string TimeZoneId, bool IsActive);
internal sealed record UpdateCampRequest(string Name, string TimeZoneId, DateOnly StartDate, DateOnly EndDate);
internal sealed record ChangeCampStatusRequest(string Status);
internal sealed record CopyClubCollectionRequest(long SourceClubId, long ExpectedRevision);
internal sealed record CopyCampCollectionRequest(long SourceClubId);

internal static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin");
        admin.MapGet("/overview", OverviewAsync);
        admin.MapGet("/administrators", AdministratorsAsync);
        admin.MapPost("/peer-selections", CreatePeerSelectionAsync);
        admin.MapGet("/peer-selections/{publicId:guid}", GetPeerSelectionAsync);
        admin.MapPost("/peer-selections/{publicId:guid}/fallback", SendFallbackAsync);
        admin.MapPost("/administrators/from-selection", AddAdministratorsAsync);
        admin.MapDelete("/administrators/{telegramUserId:long}", RemoveAdministratorAsync);
        admin.MapPost("/clubs", CreateClubAsync);
        admin.MapPut("/clubs/{clubId:long}", UpdateClubAsync);
        admin.MapPost("/clubs/{clubId:long}/collection/from-club", CopyClubCollectionAsync);
        admin.MapPost("/camps", CreateCampAsync);
        admin.MapPut("/camps/{campId:long}", UpdateCampAsync);
        admin.MapPost("/camps/{campId:long}/base-collection/from-club", CopyCampCollectionAsync);
        admin.MapPost("/camps/{campId:long}/status", ChangeCampStatusAsync);
        admin.MapGet("/exports/statistics.zip", ExportAsync);
        return group;
    }

    private static async Task<IResult> OverviewAsync(HttpRequest request, AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        CancellationToken cancellationToken)
    {
        if (await AdminIdentityAsync(request, authenticator, administrators, cancellationToken) is null)
            return Results.Forbid();
        var clubs = await dbContext.Clubs.AsNoTracking().Include(x => x.BotChat).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, TelegramTitle = x.BotChat.Name, x.BotChat.TimeZoneId,
                x.BotChat.IsActive, GameCount = x.CollectionJson, x.CollectionRevision, x.UpdatedAt,
                Gatherings = x.BotChat.Gatherings.Count })
            .ToArrayAsync(cancellationToken);
        var clubViews = clubs.Select(x => new { x.Id, x.Name, x.TelegramTitle, x.TimeZoneId, x.IsActive,
            GameCount = ClubCollectionSerializer.Deserialize(x.GameCount).Games.Count,
            x.CollectionRevision, x.UpdatedAt, x.Gatherings });
        var camps = await dbContext.Camps.AsNoTracking().Include(x => x.BotChat).Include(x => x.SourceClub)
            .OrderByDescending(x => x.StartDate).Select(x => new
            {
                x.Id, x.Name, TelegramTitle = x.BotChat.Name, x.BotChat.TimeZoneId,
                Status = x.Status.ToString(), x.StartDate, x.EndDate,
                x.SourceClubId, SourceClubName = x.SourceClub == null ? null : x.SourceClub.Name,
                Registrations = x.Registrations.Count, Contributions = x.Contributions.Count,
                Gatherings = x.BotChat.Gatherings.Count
            }).ToArrayAsync(cancellationToken);
        return Results.Ok(new { Clubs = clubViews, Camps = camps });
    }

    private static async Task<IResult> AdministratorsAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        CancellationToken cancellationToken) =>
        await AdminIdentityAsync(request, authenticator, administrators, cancellationToken) is null
            ? Results.Forbid() : Results.Ok(await administrators.ListAsync(cancellationToken));

    private static async Task<IResult> CreatePeerSelectionAsync(HttpRequest request,
        CreatePeerSelectionRequest body, TelegramMiniAppAuthenticator authenticator,
        IAdministratorStore administrators, TelegramPeerSelectionService selections,
        CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, administrators, cancellationToken);
        if (identity is null) return Results.Forbid();
        if (!Enum.TryParse<TelegramPeerSelectionPurpose>(body.Purpose, true, out var purpose))
            return MiniAppEndpointSupport.Problem("validation", "Неизвестное назначение выбора Telegram.");
        try { return Results.Ok(await selections.CreateAsync(identity.TelegramUserId, purpose, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> GetPeerSelectionAsync(HttpRequest request, Guid publicId,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        TelegramPeerSelectionService selections, CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, administrators, cancellationToken);
        if (identity is null) return Results.Forbid();
        try { return Results.Ok(await selections.GetAsync(publicId, identity.TelegramUserId, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> SendFallbackAsync(HttpRequest request, Guid publicId,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        TelegramPeerSelectionService selections, CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, administrators, cancellationToken);
        if (identity is null) return Results.Forbid();
        try
        {
            await selections.SendFallbackAsync(publicId, identity.TelegramUserId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> AddAdministratorsAsync(HttpRequest request,
        PeerSelectionTokenRequest body, TelegramMiniAppAuthenticator authenticator,
        IAdministratorStore administrators, TelegramPeerSelectionService selections,
        CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, administrators, cancellationToken);
        if (identity is null) return Results.Forbid();
        try
        {
            var result = await selections.ConsumeAsync(body.SelectionId, identity.TelegramUserId,
                TelegramPeerSelectionPurpose.AddAdministrator, cancellationToken);
            if (result.Users is null || result.Users.Count == 0)
                return MiniAppEndpointSupport.Problem("validation", "Telegram не вернул пользователей.");
            foreach (var user in result.Users)
                await administrators.AddAsync(user.TelegramUserId, user.DisplayName, user.Username,
                    identity.TelegramUserId, cancellationToken);
            return Results.Ok(await administrators.ListAsync(cancellationToken));
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> RemoveAdministratorAsync(HttpRequest request, long telegramUserId,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        CancellationToken cancellationToken)
    {
        if (await AdminIdentityAsync(request, authenticator, administrators, cancellationToken) is null)
            return Results.Forbid();
        try { await administrators.RemoveAsync(telegramUserId, cancellationToken); return Results.NoContent(); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> CreateClubAsync(HttpRequest request, CreateClubRequest body,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        TelegramPeerSelectionService selections, ManagedCommunityService communities,
        CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, administrators, cancellationToken);
        if (identity is null) return Results.Forbid();
        try
        {
            var selected = await selections.ConsumeAsync(body.SelectionId, identity.TelegramUserId,
                TelegramPeerSelectionPurpose.CreateClubChat, cancellationToken);
            var chat = selected.Chat ?? throw new InvalidOperationException("Telegram не вернул группу.");
            var club = await communities.CreateClubAsync(new(
                string.IsNullOrWhiteSpace(body.Name) ? chat.Title ?? "Новый клуб" : body.Name.Trim(),
                chat.TelegramChatId, body.TimeZoneId, identity.TelegramUserId), cancellationToken);
            return Results.Created($"/api/miniapp/admin/clubs/{club.Id}", new { club.Id });
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> UpdateClubAsync(HttpRequest request, long clubId,
        UpdateCommunityRequest body, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        IAdministratorStore administrators, CancellationToken cancellationToken)
    {
        if (await AdminIdentityAsync(request, authenticator, administrators, cancellationToken) is null)
            return Results.Forbid();
        try
        {
            _ = CommunityOptions.RequireTimeZone(body.TimeZoneId);
            var club = await dbContext.Clubs.Include(x => x.BotChat).SingleOrDefaultAsync(x => x.Id == clubId,
                cancellationToken) ?? throw new KeyNotFoundException("Клуб не найден.");
            if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Trim().Length > 160)
                throw new InvalidOperationException("Название клуба некорректно.");
            CommunityTimeZonePolicy.EnsureChangeAllowed(club.BotChat.TimeZoneId, body.TimeZoneId,
                await dbContext.GameGatherings.AnyAsync(x => x.CommunityKey == club.BotChatKey,
                    cancellationToken));
            club.Name = club.BotChat.Name = body.Name.Trim();
            club.BotChat.TimeZoneId = body.TimeZoneId;
            club.BotChat.IsActive = body.IsActive;
            club.UpdatedAt = club.BotChat.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> CreateCampAsync(HttpRequest request, CreateCampRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        TelegramPeerSelectionService selections, ManagedCommunityService communities,
        CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, administrators, cancellationToken);
        if (identity is null) return Results.Forbid();
        try
        {
            var selected = await selections.ConsumeAsync(body.SelectionId, identity.TelegramUserId,
                TelegramPeerSelectionPurpose.CreateCampChat, cancellationToken);
            var chat = selected.Chat ?? throw new InvalidOperationException("Telegram не вернул группу.");
            var timeZone = body.TimeZoneId?.Trim();
            if (string.IsNullOrWhiteSpace(timeZone) && body.SourceClubId is { } sourceClubId)
                timeZone = await dbContext.Clubs.Where(x => x.Id == sourceClubId)
                    .Select(x => x.BotChat.TimeZoneId).SingleAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(timeZone)) throw new InvalidOperationException("Выберите часовой пояс.");
            var camp = await communities.CreateCampAsync(new(
                string.IsNullOrWhiteSpace(body.Name) ? chat.Title ?? "Новый кэмп" : body.Name.Trim(),
                chat.TelegramChatId, timeZone, identity.TelegramUserId, body.SourceClubId,
                body.StartDate, body.EndDate), cancellationToken);
            return Results.Created($"/api/miniapp/admin/camps/{camp.Id}", new { camp.Id, Status = camp.Status.ToString() });
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> CopyClubCollectionAsync(HttpRequest request, long clubId,
        CopyClubCollectionRequest body, TelegramMiniAppAuthenticator authenticator,
        IAdministratorStore administrators, ClubCollectionService collections, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await AdminIdentityAsync(request, authenticator, administrators, cancellationToken) is null)
            return Results.Forbid();
        try
        {
            await collections.CopyFromClubAsync(clubId, body.SourceClubId, body.ExpectedRevision,
                timeProvider.GetUtcNow(), cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> CopyCampCollectionAsync(HttpRequest request, long campId,
        CopyCampCollectionRequest body, TelegramMiniAppAuthenticator authenticator,
        IAdministratorStore administrators, ManagedCommunityService communities,
        CancellationToken cancellationToken)
    {
        if (await AdminIdentityAsync(request, authenticator, administrators, cancellationToken) is null)
            return Results.Forbid();
        try
        {
            await communities.CopyCampBaseCollectionAsync(campId, body.SourceClubId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> ChangeCampStatusAsync(HttpRequest request, long campId,
        ChangeCampStatusRequest body, TelegramMiniAppAuthenticator authenticator,
        IAdministratorStore administrators, ManagedCommunityService communities,
        GatheringPublicationService publication,
        GatheringNotificationService notifications,
        CancellationToken cancellationToken)
    {
        if (await AdminIdentityAsync(request, authenticator, administrators, cancellationToken) is null)
            return Results.Forbid();
        if (!Enum.TryParse<CampStatus>(body.Status, true, out var status))
            return MiniAppEndpointSupport.Problem("validation", "Неизвестный статус кэмпа.");
        try
        {
            var result = await communities.SetCampStatusAsync(campId, status, cancellationToken);
            foreach (var gatheringId in result.CancelledGatheringIds)
            {
                await publication.PublishAsync(gatheringId, cancellationToken);
                await notifications.NotifyCancellationAsync(gatheringId, cancellationToken);
            }
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> UpdateCampAsync(HttpRequest request, long campId,
        UpdateCampRequest body, TelegramMiniAppAuthenticator authenticator,
        IAdministratorStore administrators, ManagedCommunityService communities,
        CancellationToken cancellationToken)
    {
        if (await AdminIdentityAsync(request, authenticator, administrators, cancellationToken) is null)
            return Results.Forbid();
        try
        {
            await communities.UpdateCampAsync(campId,
                new(body.Name, body.TimeZoneId, body.StartDate, body.EndDate), cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> ExportAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        CsvExportService exports, CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, administrators, cancellationToken);
        if (identity is null) return Results.Forbid();
        var files = await exports.CreateAllAsync(identity.TelegramUserId, cancellationToken);
        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var file in files)
            {
                using (file.Content)
                await using (var target = archive.CreateEntry(file.FileName).Open())
                    await file.Content.CopyToAsync(target, cancellationToken);
            }
        }
        return Results.File(output.ToArray(), "application/zip", "oyinq-statistics.zip");
    }

    private static Task<TelegramMiniAppIdentity?> AdminIdentityAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        CancellationToken cancellationToken) => MiniAppEndpointSupport.AuthenticateAdminAsync(
            request, authenticator, administrators, cancellationToken);
}
