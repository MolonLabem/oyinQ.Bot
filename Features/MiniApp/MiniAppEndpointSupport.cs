using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record MiniAppCommunityAccess(TelegramMiniAppIdentity Identity, BotCommunity Community);

internal static class MiniAppEndpointSupport
{
    public static TelegramMiniAppIdentity? Authenticate(HttpRequest request, TelegramMiniAppAuthenticator authenticator) =>
        authenticator.Authenticate(request.Headers["X-Telegram-Init-Data"].FirstOrDefault());

    public static async Task<TelegramMiniAppIdentity?> AuthenticateAdminPanelAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var identity = Authenticate(request, authenticator);
        return identity is not null && await authorization.CanOpenAdminPanelAsync(identity.TelegramUserId, cancellationToken)
            ? identity : null;
    }

    public static async Task<TelegramMiniAppIdentity?> AuthenticateCommunityAdminAsync(HttpRequest request,
        string communityKey, TelegramMiniAppAuthenticator authenticator,
        IAdminAuthorizationService authorization, CancellationToken cancellationToken)
    {
        var identity = Authenticate(request, authenticator);
        return identity is not null && await authorization.CanAdministerCommunityAsync(
            identity.TelegramUserId, communityKey, cancellationToken) ? identity : null;
    }

    public static TelegramMiniAppIdentity? AuthenticateSuperAdmin(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization)
    {
        var identity = Authenticate(request, authenticator);
        return identity is not null && authorization.IsSuperAdmin(identity.TelegramUserId) ? identity : null;
    }

    public static async Task<MiniAppCommunityAccess?> AuthorizeCommunityAsync(HttpRequest request, string communityKey,
        TelegramMiniAppAuthenticator authenticator, CommunityContextResolver resolver,
        CancellationToken cancellationToken)
    {
        var identity = Authenticate(request, authenticator);
        if (identity is null) return null;
        var community = await resolver.ResolveAuthorizedAsync(communityKey, identity.TelegramUserId, cancellationToken);
        return community is null ? null : new(identity, community);
    }

    public static Task<Participant> GetOrCreateParticipantAsync(AppDbContext dbContext,
        TelegramMiniAppIdentity identity, string communityKey, CancellationToken cancellationToken) =>
        new ParticipantIdentityService(dbContext, TimeProvider.System).GetOrCreateAsync(
            identity.TelegramUserId, identity.TelegramUsername, identity.DisplayName, communityKey, cancellationToken);

    public static IResult Problem(string code, string message, int status = 400) =>
        Results.Json(new { code, message }, statusCode: status);

    public static IResult FromException(Exception exception) => exception switch
    {
        oyinQ.Bot.Features.Gatherings.GatheringPlayConflictException conflict => Problem("play_outcome_conflict", conflict.Message, 409),
        oyinQ.Bot.Features.Gatherings.GatheringScheduleConflictException conflict => Results.Json(new
        { code = "gathering_schedule_conflict", message = conflict.Message, conflicts = conflict.Conflicts }, statusCode: 409),
        CampAttendanceDateRequiredException attendance => Results.Json(new
        {
            code = "camp_attendance_date_required",
            message = attendance.Message,
            requiredDate = attendance.Date
        }, statusCode: StatusCodes.Status409Conflict),
        KeyNotFoundException => Problem("not_found", exception.Message, StatusCodes.Status404NotFound),
        UnauthorizedAccessException => Problem("forbidden", exception.Message, StatusCodes.Status403Forbidden),
        InvalidOperationException => Problem("invalid_operation", exception.Message),
        ArgumentException => Problem("validation", exception.Message),
        _ => throw exception
    };
}
