using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record MiniAppCommunityAccess(TelegramMiniAppIdentity Identity, BotCommunity Community);

internal static class MiniAppEndpointSupport
{
    public static TelegramMiniAppIdentity? Authenticate(HttpRequest request, TelegramMiniAppAuthenticator authenticator) =>
        authenticator.Authenticate(request.Headers["X-Telegram-Init-Data"].FirstOrDefault());

    public static async Task<TelegramMiniAppIdentity?> AuthenticateAdminAsync(HttpRequest request,
        TelegramMiniAppAuthenticator authenticator, IAdministratorStore administrators,
        CancellationToken cancellationToken)
    {
        var identity = Authenticate(request, authenticator);
        return identity is not null && await administrators.IsAdministratorAsync(identity.TelegramUserId, cancellationToken)
            ? identity : null;
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

    public static async Task<Participant> GetOrCreateParticipantAsync(AppDbContext dbContext,
        TelegramMiniAppIdentity identity, string communityKey, CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            x => x.TelegramUserId == identity.TelegramUserId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (participant is null)
        {
            participant = new Participant
            {
                TelegramUserId = identity.TelegramUserId,
                TelegramUsername = identity.TelegramUsername,
                DisplayName = identity.DisplayName ?? $"Telegram {identity.TelegramUserId}",
                CreatedAt = now
            };
            dbContext.Participants.Add(participant);
        }
        participant.ActiveCommunityKey = communityKey;
        participant.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return participant;
    }

    public static IResult Problem(string code, string message, int status = 400) =>
        Results.Json(new { code, message }, statusCode: status);

    public static IResult FromException(Exception exception) => exception switch
    {
        KeyNotFoundException => Problem("not_found", exception.Message, StatusCodes.Status404NotFound),
        UnauthorizedAccessException => Problem("forbidden", exception.Message, StatusCodes.Status403Forbidden),
        InvalidOperationException => Problem("invalid_operation", exception.Message),
        ArgumentException => Problem("validation", exception.Message),
        _ => throw exception
    };
}
