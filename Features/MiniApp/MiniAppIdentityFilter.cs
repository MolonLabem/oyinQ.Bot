using oyinQ.Bot.Integrations.Telegram;

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
        return await next(context);
    }
}
