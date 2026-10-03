using oyinQ.Bot.Integrations.Telegram;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Data;

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
        await using var operation = await ParticipantOperationLock.AcquireAsync(
            services.GetRequiredService<AppDbContext>(), identity.TelegramUserId, context.HttpContext.RequestAborted);
        try
        {
            if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ProfileLifecycleEndpoint>() is null)
                await services.GetRequiredService<ParticipantIdentityService>().GetOrCreateAsync(
                    identity.TelegramUserId, identity.TelegramUsername, identity.DisplayName, null,
                    context.HttpContext.RequestAborted);
            return await next(context);
        }
        catch (ProfileDeletedException exception)
        {
            return MiniAppEndpointSupport.Problem("profile_deleted", exception.Message, StatusCodes.Status410Gone);
        }
        catch (CommunityMembershipUnavailableException exception)
        {
            return MiniAppEndpointSupport.Problem("telegram_unavailable", exception.Message,
                StatusCodes.Status503ServiceUnavailable);
        }
    }
}
