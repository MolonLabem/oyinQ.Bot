using oyinQ.Bot.Integrations.Telegram;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.MiniApp;

public sealed class MiniAppIdentityFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "private, no-store";
        var services = context.HttpContext.RequestServices;
        var identity = MiniAppEndpointSupport.Authenticate(context.HttpContext.Request,
            services.GetRequiredService<TelegramMiniAppAuthenticator>());
        if (identity is null) return Results.Unauthorized();
        await services.GetRequiredService<ParticipantIdentityService>().GetOrCreateAsync(
            identity.TelegramUserId, identity.TelegramUsername, identity.DisplayName, null,
            context.HttpContext.RequestAborted);
        try { return await next(context); }
        catch (CommunityMembershipUnavailableException exception)
        {
            return MiniAppEndpointSupport.Problem("telegram_unavailable", exception.Message,
                StatusCodes.Status503ServiceUnavailable);
        }
    }
}
