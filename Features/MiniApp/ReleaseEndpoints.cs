using oyinQ.Bot.Features.Admin;
namespace oyinQ.Bot.Features.MiniApp;

internal static class ReleaseEndpoints
{
    internal sealed record PublishRequest(string ReleaseId, IReadOnlyCollection<string> CommunityKeys, bool Confirmed, bool RetryFailed = false);
    internal sealed record CustomMessageRequest(Guid RequestId, string Text);
    internal sealed record QueueMessageRequest(IReadOnlyCollection<string> CommunityKeys, bool Confirmed, bool RetryFailed = false);
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
        group.MapPost("/admin/announcements", async (HttpRequest request, CustomMessageRequest body, TelegramMiniAppAuthenticator auth, ReleaseAnnouncementService service, CancellationToken ct) =>
        {
            var identity = MiniAppEndpointSupport.Authenticate(request, auth);
            if (identity is null) return Results.Unauthorized();
            try { return Results.Ok(new { Id = await service.SaveCustomAsync(identity.TelegramUserId, body.RequestId, body.Text, ct) }); }
            catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
        });
        group.MapGet("/admin/announcements", async (HttpRequest request, int? page, TelegramMiniAppAuthenticator auth, ReleaseAnnouncementService service, CancellationToken ct) =>
        {
            var identity = MiniAppEndpointSupport.Authenticate(request, auth);
            if (identity is null) return Results.Unauthorized();
            try { return Results.Ok(await service.CustomHistoryAsync(identity.TelegramUserId, page ?? 1, ct)); }
            catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
        });
        group.MapGet("/admin/announcements/{id:guid}", async (HttpRequest request, Guid id, TelegramMiniAppAuthenticator auth, ReleaseAnnouncementService service, CancellationToken ct) =>
        {
            var identity = MiniAppEndpointSupport.Authenticate(request, auth);
            if (identity is null) return Results.Unauthorized();
            try { return Results.Ok(await service.PreviewCustomAsync(identity.TelegramUserId, id, ct)); }
            catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
        });
        group.MapPost("/admin/announcements/{id:guid}/queue", async (HttpRequest request, Guid id, QueueMessageRequest body, TelegramMiniAppAuthenticator auth, ReleaseAnnouncementService service, CancellationToken ct) =>
        {
            var identity = MiniAppEndpointSupport.Authenticate(request, auth);
            if (identity is null) return Results.Unauthorized();
            try { await service.QueueCustomAsync(identity.TelegramUserId, id, body.CommunityKeys, body.Confirmed, body.RetryFailed, ct); return Results.NoContent(); }
            catch (Exception e) { return MiniAppEndpointSupport.FromException(e); }
        });
        return group;
    }
}
