using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record SaveProfileRequest(string CommunityKey, string? DisplayName);

internal static class ProfileEndpoints
{
    public static RouteGroupBuilder MapProfileEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/profile", GetAsync);
        group.MapPut("/profile", SaveAsync);
        return group;
    }

    private static async Task<IResult> GetAsync(HttpRequest request, string community,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, community, authenticator,
            resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        var participant = await MiniAppEndpointSupport.GetOrCreateParticipantAsync(dbContext, access.Identity,
            community, cancellationToken);
        return Results.Ok(Present(participant));
    }

    private static async Task<IResult> SaveAsync(HttpRequest request, SaveProfileRequest body,
        AppDbContext dbContext, TelegramMiniAppAuthenticator authenticator,
        CommunityContextResolver resolver, CancellationToken cancellationToken)
    {
        var access = await MiniAppEndpointSupport.AuthorizeCommunityAsync(request, body.CommunityKey,
            authenticator, resolver, cancellationToken);
        if (access is null) return Results.Forbid();
        var participant = await MiniAppEndpointSupport.GetOrCreateParticipantAsync(dbContext, access.Identity,
            body.CommunityKey, cancellationToken);
        var displayName = body.DisplayName?.Trim();
        if (displayName?.Length > 128)
            return MiniAppEndpointSupport.Problem("validation", "Имя не должно быть длиннее 128 символов.");
        participant.PreferredDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        participant.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(Present(participant));
    }

    private static object Present(Participant participant) => new
    {
        DisplayName = ParticipantPresentation.GetDisplayName(participant),
        participant.PreferredDisplayName,
        TelegramDisplayName = participant.DisplayName,
        participant.TelegramUsername
    };
}
