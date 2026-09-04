using oyinQ.Bot.Features.Admin;
namespace oyinQ.Bot.Features.MiniApp;

internal static class ReleaseEndpoints
{
    internal sealed record PublishRequest(string ReleaseId, IReadOnlyCollection<string> CommunityKeys, bool Confirmed, bool RetryFailed = false);
    public static RouteGroupBuilder MapReleaseEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/admin/release", async (HttpRequest request, TelegramMiniAppAuthenticator auth, ReleaseAnnouncementService service, CancellationToken ct) =>
        {
            var identity = MiniAppEndpointSupport.Authenticate(request, auth);
            if (identity is null) return Results.Unauthorized();
            try { return Results.Ok(await service.PreviewAsync(identity.TelegramUserId, ct)); }
            catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
        });
        group.MapPost("/admin/release", async (HttpRequest request, PublishRequest body, TelegramMiniAppAuthenticator auth, ReleaseAnnouncementService service, CancellationToken ct) =>
        {
            var identity = MiniAppEndpointSupport.Authenticate(request, auth);
            if (identity is null) return Results.Unauthorized();
            try { await service.QueueAsync(identity.TelegramUserId, body.ReleaseId, body.CommunityKeys, body.Confirmed, body.RetryFailed, ct); return Results.NoContent(); }
            catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
        });
        return group;
    }
}
