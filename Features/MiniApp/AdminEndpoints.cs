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

internal sealed record CreatePeerSelectionRequest(string Purpose, string? CommunityKey);
internal sealed record PeerSelectionTokenRequest(Guid SelectionId, string CommunityKey);
internal sealed record CreateClubRequest(Guid? SelectionId, long? KnownTelegramChatId, string? Name,
    string TimeZoneId);
internal sealed record CreateCampRequest(Guid? SelectionId, long? KnownTelegramChatId, string? Name,
    string StartsAtLocal, string EndsAtLocal,
    long? SourceClubId, string? TimeZoneId);
internal sealed record UpdateCommunityRequest(string Name, string TimeZoneId, bool IsActive);
internal sealed record UpdateCampRequest(string Name, string TimeZoneId, string StartsAtLocal, string EndsAtLocal);
internal sealed record ChangeCampStatusRequest(string Status);
internal sealed record CopyClubCollectionRequest(long SourceClubId, long ExpectedRevision);
internal sealed record CopyCampCollectionRequest(long SourceClubId);
internal sealed record UpdatePostingTopicRequest(int? MessageThreadId);

internal static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin");
        admin.MapGet("/overview", OverviewAsync);
        admin.MapGet("/communities/{communityKey}/administrators", AdministratorsAsync);
        admin.MapGet("/communities/{communityKey}/administrator-candidates", AdministratorCandidatesAsync);
        admin.MapGet("/communities/{communityKey}/posting-topic", PostingTopicAsync);
        admin.MapPut("/communities/{communityKey}/posting-topic", UpdatePostingTopicAsync);
        admin.MapPost("/communities/{communityKey}/administrators/{telegramUserId:long}",
            AddAdministratorCandidateAsync);
        admin.MapPost("/peer-selections", CreatePeerSelectionAsync);
        admin.MapGet("/peer-selections/{publicId:guid}", GetPeerSelectionAsync);
        admin.MapPost("/peer-selections/{publicId:guid}/fallback", SendFallbackAsync);
        admin.MapPost("/administrators/from-selection", AddAdministratorsAsync);
        admin.MapDelete("/communities/{communityKey}/administrators/{telegramUserId:long}", RemoveAdministratorAsync);
        admin.MapPost("/clubs", CreateClubAsync);
        admin.MapPut("/clubs/{clubId:long}", UpdateClubAsync);
        admin.MapDelete("/clubs/{clubId:long}", DeleteClubAsync);
        admin.MapPost("/clubs/{clubId:long}/collection/from-club", CopyClubCollectionAsync);
        admin.MapPost("/camps", CreateCampAsync);
        admin.MapPut("/camps/{campId:long}", UpdateCampAsync);
        admin.MapDelete("/camps/{campId:long}", DeleteCampAsync);
        admin.MapGet("/camps/{campId:long}/participants", CampParticipantsAsync);
        admin.MapPost("/camps/{campId:long}/participants/send-to-me", SendCampParticipantsToMeAsync);
        admin.MapPost("/camps/{campId:long}/base-collection/from-club", CopyCampCollectionAsync);
        admin.MapPost("/camps/{campId:long}/status", ChangeCampStatusAsync);
        admin.MapGet("/exports/statistics.zip", ExportAsync);
        return group;
    }

    private static async Task<IResult> CampParticipantsAsync(HttpRequest request, long campId,
        TelegramMiniAppAuthenticator authenticator, CampParticipantAdminService participants,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        try { return Results.Ok(await participants.GetAsync(identity.TelegramUserId, campId, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static Task<IResult> DeleteClubAsync(HttpRequest request, long clubId,
        TelegramMiniAppAuthenticator authenticator, CommunityDeletionService deletion,
        GatheringPublicationService publication,
        CancellationToken cancellationToken) =>
        DeleteCommunityAsync(request, authenticator,
            (actor, token) => deletion.DeleteClubAsync(actor, clubId, token), publication,
            cancellationToken);

    private static Task<IResult> DeleteCampAsync(HttpRequest request, long campId,
        TelegramMiniAppAuthenticator authenticator, CommunityDeletionService deletion,
        GatheringPublicationService publication,
        CancellationToken cancellationToken) =>
        DeleteCommunityAsync(request, authenticator,
            (actor, token) => deletion.DeleteCampAsync(actor, campId, token), publication,
            cancellationToken);

    private static async Task<IResult> DeleteCommunityAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator,
        Func<long, CancellationToken, Task<CommunityDeletionResult>> delete,
        GatheringPublicationService publication,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        try
        {
            var result = await delete(identity.TelegramUserId, cancellationToken);
            foreach (var gatheringId in result.CancelledGatheringIds)
            {
                await publication.PublishAsync(gatheringId, cancellationToken);
            }
            return Results.Ok(new { result.AlreadyDeleted, result.Name });
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> SendCampParticipantsToMeAsync(HttpRequest request, long campId,
        TelegramMiniAppAuthenticator authenticator, CampParticipantAdminService participants,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        try
        {
            return Results.Ok(await participants.SendToActorAsync(identity.TelegramUserId, campId,
                cancellationToken));
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> PostingTopicAsync(HttpRequest request, string communityKey,
        TelegramMiniAppAuthenticator authenticator, PostingTopicService postingTopics,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        try
        {
            return Results.Ok(await postingTopics.GetAsync(identity.TelegramUserId, communityKey,
                cancellationToken));
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> UpdatePostingTopicAsync(HttpRequest request, string communityKey,
        UpdatePostingTopicRequest body, TelegramMiniAppAuthenticator authenticator,
        PostingTopicService postingTopics, CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        try
        {
            await postingTopics.SetAsync(identity.TelegramUserId, communityKey, body.MessageThreadId,
                cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> OverviewAsync(HttpRequest request, AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, authorization, cancellationToken);
        if (identity is null)
            return Results.Forbid();
        var access = await authorization.GetAdminPanelChatsAsync(identity.TelegramUserId, cancellationToken);
        var approvedKeys = access.Where(x => x.IsApproved && x.CommunityKey is not null)
            .Select(x => x.CommunityKey!).ToHashSet();
        var clubs = await dbContext.Clubs.AsNoTracking().Include(x => x.BotChat)
            .Where(x => approvedKeys.Contains(x.BotChatKey)).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.BotChatKey, x.Name, TelegramTitle = x.BotChat.Name, x.BotChat.TimeZoneId,
                x.BotChat.TelegramChatId, x.BotChat.IsActive, GameCount = x.CollectionJson, x.CollectionRevision, x.UpdatedAt,
                Gatherings = x.BotChat.Gatherings.Count })
            .ToArrayAsync(cancellationToken);
        var clubViews = clubs.Select(x => new { x.Id, CommunityKey = access.Single(a => a.CommunityKey == x.BotChatKey).CommunityKey,
            x.Name, x.TelegramTitle, x.TelegramChatId, x.TimeZoneId, x.IsActive, IsApproved = true,
            GameCount = ClubCollectionSerializer.Deserialize(x.GameCount).Games.Count,
            x.CollectionRevision, x.UpdatedAt, x.Gatherings });
        var camps = await dbContext.Camps.AsNoTracking().Include(x => x.BotChat).Include(x => x.SourceClub)
            .Where(x => approvedKeys.Contains(x.BotChatKey))
            .OrderByDescending(x => x.StartsAtUtc).Select(x => new
            {
                x.Id, CommunityKey = x.BotChatKey, x.Name, TelegramTitle = x.BotChat.Name,
                x.BotChat.TelegramChatId, x.BotChat.TimeZoneId, IsApproved = true,
                Status = x.Status.ToString(), x.StartsAtUtc, x.EndsAtUtc,
                SourceClubId = x.SourceClub != null && approvedKeys.Contains(x.SourceClub.BotChatKey)
                    ? x.SourceClubId : null,
                SourceClubName = x.SourceClub != null && approvedKeys.Contains(x.SourceClub.BotChatKey)
                    ? x.SourceClub.Name : null,
                Registrations = x.Registrations.Count, Contributions = x.Contributions.Count,
                Gatherings = x.BotChat.Gatherings.Count
            }).ToArrayAsync(cancellationToken);
        var locked = access.Where(x => !x.IsApproved).Select(x => new
        {
            x.CommunityKey, x.TelegramChatId, x.Name, x.Mode, x.IsActive, IsApproved = false
        });
        return Results.Ok(new { Clubs = clubViews, Camps = camps, LockedCommunities = locked,
            IsSuperAdmin = authorization.IsSuperAdmin(identity.TelegramUserId) });
    }

    private static async Task<IResult> AdministratorsAsync(HttpRequest request, string communityKey,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null || !await authorization.CanManageAdminsAsync(identity.TelegramUserId,
                communityKey, cancellationToken)) return Results.Forbid();
        return Results.Ok(await authorization.ListGroupAdminsAsync(identity.TelegramUserId, communityKey,
            cancellationToken));
    }

    private static async Task<IResult> AdministratorCandidatesAsync(HttpRequest request, string communityKey,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        try
        {
            return Results.Ok(await authorization.ListEligibleGroupAdminsAsync(identity.TelegramUserId,
                communityKey, cancellationToken));
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> AddAdministratorCandidateAsync(HttpRequest request, string communityKey,
        long telegramUserId, TelegramMiniAppAuthenticator authenticator,
        IAdminAuthorizationService authorization, CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        try
        {
            await authorization.GrantEligibleGroupAdminAsync(identity.TelegramUserId, communityKey,
                telegramUserId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> CreatePeerSelectionAsync(HttpRequest request,
        CreatePeerSelectionRequest body, TelegramMiniAppAuthenticator authenticator,
        IAdminAuthorizationService authorization, TelegramPeerSelectionService selections,
        CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, authorization, cancellationToken);
        if (identity is null) return Results.Forbid();
        if (!Enum.TryParse<TelegramPeerSelectionPurpose>(body.Purpose, true, out var purpose))
            return MiniAppEndpointSupport.Problem("validation", "Неизвестное назначение выбора Telegram.");
        if (purpose is TelegramPeerSelectionPurpose.CreateClubChat or TelegramPeerSelectionPurpose.CreateCampChat)
        {
            if (!authorization.IsSuperAdmin(identity.TelegramUserId)) return Results.Forbid();
        }
        else if (purpose == TelegramPeerSelectionPurpose.AddAdministrator
                 && (string.IsNullOrWhiteSpace(body.CommunityKey)
                     || !await authorization.CanManageAdminsAsync(identity.TelegramUserId, body.CommunityKey,
                         cancellationToken))) return Results.Forbid();
        try { return Results.Ok(await selections.CreateAsync(identity.TelegramUserId, purpose, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> GetPeerSelectionAsync(HttpRequest request, Guid publicId,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        TelegramPeerSelectionService selections, CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, authorization, cancellationToken);
        if (identity is null) return Results.Forbid();
        try { return Results.Ok(await selections.GetAsync(publicId, identity.TelegramUserId, cancellationToken)); }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> SendFallbackAsync(HttpRequest request, Guid publicId,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        TelegramPeerSelectionService selections, CancellationToken cancellationToken)
    {
        var identity = await AdminIdentityAsync(request, authenticator, authorization, cancellationToken);
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
        IAdminAuthorizationService authorization, TelegramPeerSelectionService selections,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        if (!await authorization.CanManageAdminsAsync(identity.TelegramUserId, body.CommunityKey,
                cancellationToken)) return Results.Forbid();
        try
        {
            var result = await selections.ConsumeAsync(body.SelectionId, identity.TelegramUserId,
                TelegramPeerSelectionPurpose.AddAdministrator, cancellationToken);
            if (result.Users is null || result.Users.Count == 0)
                return MiniAppEndpointSupport.Problem("validation", "Telegram не вернул пользователей.");
            foreach (var user in result.Users)
                await authorization.GrantGroupAdminAsync(identity.TelegramUserId, body.CommunityKey,
                    user.TelegramUserId, user.DisplayName, user.Username, cancellationToken);
            return Results.Ok(await authorization.ListGroupAdminsAsync(identity.TelegramUserId,
                body.CommunityKey, cancellationToken));
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> RemoveAdministratorAsync(HttpRequest request, string communityKey,
        long telegramUserId, TelegramMiniAppAuthenticator authenticator,
        IAdminAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        try
        {
            await authorization.RevokeGroupAdminAsync(identity.TelegramUserId, communityKey,
                telegramUserId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> CreateClubAsync(HttpRequest request, CreateClubRequest body,
        AppDbContext dbContext,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        TelegramPeerSelectionService selections, ManagedCommunityService communities,
        ITelegramCommunityOnboardingService onboarding,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.AuthenticateSuperAdmin(request, authenticator, authorization);
        if (identity is null) return Results.Forbid();
        try
        {
            var chat = await ResolveSelectedChatAsync(body.SelectionId, body.KnownTelegramChatId,
                identity.TelegramUserId, TelegramPeerSelectionPurpose.CreateClubChat, dbContext, selections,
                cancellationToken);
            var club = await communities.CreateClubAsync(new(
                string.IsNullOrWhiteSpace(body.Name) ? chat.Title ?? "Новый клуб" : body.Name.Trim(),
                chat.TelegramChatId, body.TimeZoneId, identity.TelegramUserId,
                RequireCreatorTelegramAdmin: false), cancellationToken);
            var delivery = await onboarding.SendAsync(chat.TelegramChatId, cancellationToken);
            return Results.Created($"/api/miniapp/admin/clubs/{club.Id}", new
            {
                club.Id,
                TelegramOnboardingSent = delivery.Sent,
                delivery.Warning
            });
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> UpdateClubAsync(HttpRequest request, long clubId,
        UpdateCommunityRequest body, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        IAdminAuthorizationService authorization, CancellationToken cancellationToken)
    {
        var clubKey = await dbContext.Clubs.AsNoTracking().Where(x => x.Id == clubId)
            .Select(x => x.BotChatKey).SingleOrDefaultAsync(cancellationToken);
        if (clubKey is null || await MiniAppEndpointSupport.AuthenticateCommunityAdminAsync(request, clubKey,
                authenticator, authorization, cancellationToken) is null) return Results.Forbid();
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
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        TelegramPeerSelectionService selections, ManagedCommunityService communities,
        ITelegramCommunityOnboardingService onboarding,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.AuthenticateSuperAdmin(request, authenticator, authorization);
        if (identity is null) return Results.Forbid();
        try
        {
            var chat = await ResolveSelectedChatAsync(body.SelectionId, body.KnownTelegramChatId,
                identity.TelegramUserId, TelegramPeerSelectionPurpose.CreateCampChat, dbContext, selections,
                cancellationToken);
            var timeZone = body.TimeZoneId?.Trim();
            if (string.IsNullOrWhiteSpace(timeZone) && body.SourceClubId is { } sourceClubId)
                timeZone = await dbContext.Clubs.Where(x => x.Id == sourceClubId)
                    .Select(x => x.BotChat.TimeZoneId).SingleAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(timeZone)) throw new InvalidOperationException("Выберите часовой пояс.");
            var camp = await communities.CreateCampAsync(new(
                string.IsNullOrWhiteSpace(body.Name) ? chat.Title ?? "Новый кэмп" : body.Name.Trim(),
                chat.TelegramChatId, timeZone, identity.TelegramUserId, body.SourceClubId,
                CommunityTime.ParseLocal(body.StartsAtLocal, timeZone), CommunityTime.ParseLocal(body.EndsAtLocal, timeZone), RequireCreatorTelegramAdmin: false), cancellationToken);
            var delivery = await onboarding.SendAsync(chat.TelegramChatId, cancellationToken);
            return Results.Created($"/api/miniapp/admin/camps/{camp.Id}", new
            {
                camp.Id,
                Status = camp.Status.ToString(),
                TelegramOnboardingSent = delivery.Sent,
                delivery.Warning
            });
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<SelectedTelegramChat> ResolveSelectedChatAsync(Guid? selectionId,
        long? knownTelegramChatId, long superAdminTelegramUserId, TelegramPeerSelectionPurpose purpose,
        AppDbContext dbContext, TelegramPeerSelectionService selections, CancellationToken cancellationToken)
    {
        if (knownTelegramChatId is { } chatId)
        {
            if (selectionId is not null)
                throw new InvalidOperationException("Укажите один источник Telegram-группы.");
            var known = await dbContext.KnownTelegramChats.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TelegramChatId == chatId && x.IsBotPresent, cancellationToken)
                ?? throw new InvalidOperationException("Бот больше не состоит в выбранной Telegram-группе.");
            if (await dbContext.OyinQCommunities.AsNoTracking()
                    .AnyAsync(x => x.TelegramChatId == chatId && x.DeletedAt == null, cancellationToken))
                throw new InvalidOperationException("Эта Telegram-группа уже настроена в OyinQ.");
            return new SelectedTelegramChat(known.TelegramChatId, known.Title, known.Username);
        }
        if (selectionId is null) throw new InvalidOperationException("Выберите Telegram-группу.");
        var selected = await selections.ConsumeAsync(selectionId.Value, superAdminTelegramUserId, purpose,
            cancellationToken);
        return selected.Chat ?? throw new InvalidOperationException("Telegram не вернул группу.");
    }

    private static async Task<IResult> CopyClubCollectionAsync(HttpRequest request, long clubId,
        CopyClubCollectionRequest body, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        IAdminAuthorizationService authorization, ClubCollectionService collections, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        var keys = await dbContext.Clubs.AsNoTracking()
            .Where(x => x.Id == clubId || x.Id == body.SourceClubId)
            .Select(x => new { x.Id, x.BotChatKey }).ToArrayAsync(cancellationToken);
        if (identity is null || keys.Length != (clubId == body.SourceClubId ? 1 : 2)
            || !await authorization.CanAdministerCommunityAsync(identity.TelegramUserId,
                keys.Single(x => x.Id == clubId).BotChatKey, cancellationToken)
            || !await authorization.CanAdministerCommunityAsync(identity.TelegramUserId,
                keys.Single(x => x.Id == body.SourceClubId).BotChatKey, cancellationToken)) return Results.Forbid();
        try
        {
            await collections.CopyFromClubAsync(clubId, body.SourceClubId, body.ExpectedRevision,
                timeProvider.GetUtcNow(), cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> CopyCampCollectionAsync(HttpRequest request, long campId,
        CopyCampCollectionRequest body, AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        IAdminAuthorizationService authorization, ManagedCommunityService communities,
        CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        var campKey = await dbContext.Camps.AsNoTracking().Where(x => x.Id == campId)
            .Select(x => x.BotChatKey).SingleOrDefaultAsync(cancellationToken);
        var sourceKey = await dbContext.Clubs.AsNoTracking().Where(x => x.Id == body.SourceClubId)
            .Select(x => x.BotChatKey).SingleOrDefaultAsync(cancellationToken);
        if (identity is null || campKey is null || sourceKey is null
            || !await authorization.CanAdministerCommunityAsync(identity.TelegramUserId, campKey, cancellationToken)
            || !await authorization.CanAdministerCommunityAsync(identity.TelegramUserId, sourceKey,
                cancellationToken)) return Results.Forbid();
        try
        {
            await communities.CopyCampBaseCollectionAsync(campId, body.SourceClubId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> ChangeCampStatusAsync(HttpRequest request, long campId,
        ChangeCampStatusRequest body, TelegramMiniAppAuthenticator authenticator,
        AppDbContext dbContext, IAdminAuthorizationService authorization, ManagedCommunityService communities,
        GatheringPublicationService publication,
        CancellationToken cancellationToken)
    {
        var campKey = await dbContext.Camps.AsNoTracking().Where(x => x.Id == campId)
            .Select(x => x.BotChatKey).SingleOrDefaultAsync(cancellationToken);
        if (campKey is null || await MiniAppEndpointSupport.AuthenticateCommunityAdminAsync(request, campKey,
                authenticator, authorization, cancellationToken) is null) return Results.Forbid();
        if (!Enum.TryParse<CampStatus>(body.Status, true, out var status))
            return MiniAppEndpointSupport.Problem("validation", "Неизвестный статус кэмпа.");
        if (status is not CampStatus.Active and not CampStatus.Cancelled)
            return MiniAppEndpointSupport.Problem("validation",
                "Завершение кэмпа происходит автоматически после даты окончания. Вручную можно только активировать или отменить проведение.");
        try
        {
            var result = await communities.SetCampStatusAsync(campId, status, cancellationToken);
            foreach (var gatheringId in result.CancelledGatheringIds)
            {
                await publication.PublishAsync(gatheringId, cancellationToken);
            }
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> UpdateCampAsync(HttpRequest request, long campId,
        UpdateCampRequest body, TelegramMiniAppAuthenticator authenticator,
        AppDbContext dbContext, IAdminAuthorizationService authorization, ManagedCommunityService communities,
        CancellationToken cancellationToken)
    {
        var campKey = await dbContext.Camps.AsNoTracking().Where(x => x.Id == campId)
            .Select(x => x.BotChatKey).SingleOrDefaultAsync(cancellationToken);
        if (campKey is null || await MiniAppEndpointSupport.AuthenticateCommunityAdminAsync(request, campKey,
                authenticator, authorization, cancellationToken) is null) return Results.Forbid();
        try
        {
            await communities.UpdateCampAsync(campId,
                new(body.Name, body.TimeZoneId, CommunityTime.ParseLocal(body.StartsAtLocal, body.TimeZoneId), CommunityTime.ParseLocal(body.EndsAtLocal, body.TimeZoneId)), cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }

    private static async Task<IResult> ExportAsync(HttpRequest request, string? community,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        CsvExportService exports, CancellationToken cancellationToken)
    {
        var identity = MiniAppEndpointSupport.Authenticate(request, authenticator);
        if (identity is null) return Results.Forbid();
        if (community is null && !authorization.IsSuperAdmin(identity.TelegramUserId)) return Results.Forbid();
        if (community is not null && !await authorization.CanAdministerCommunityAsync(identity.TelegramUserId,
                community, cancellationToken)) return Results.Forbid();
        var files = await exports.CreateAllAsync(identity.TelegramUserId, community, cancellationToken);
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
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        CancellationToken cancellationToken) => MiniAppEndpointSupport.AuthenticateAdminPanelAsync(
            request, authenticator, authorization, cancellationToken);
}
