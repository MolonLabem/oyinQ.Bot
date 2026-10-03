using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.MiniApp;

internal sealed record CampPricingPreviewRequest(CampConfiguration Configuration, int Days,
    bool NeedsAccommodation, IReadOnlyDictionary<string, string>? Answers, string TimeZoneId,
    DateOnly? RegistrationDate = null);

internal static class CampPricingPreviewEndpoints
{
    public static RouteGroupBuilder MapCampPricingPreviewEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapPost("/camps/pricing-preview", PreviewAsync);
        return admin;
    }

    private static async Task<IResult> PreviewAsync(HttpRequest request, CampPricingPreviewRequest body,
        TelegramMiniAppAuthenticator authenticator, IAdminAuthorizationService authorization,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        if (await MiniAppEndpointSupport.AuthenticateAdminPanelAsync(request, authenticator, authorization,
                cancellationToken) is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            if (body.Days is < 1 or > 366) throw new ArgumentException("Выберите от 1 до 366 дней участия.");
            var configuration = CampConfigurationRules.Normalize(body.Configuration);
            var answers = CampConfigurationRules.ValidateAnswers(configuration, body.Answers, requireAll: false);
            var instant = body.RegistrationDate is { } date
                ? CommunityTime.ParseLocal($"{date:yyyy-MM-dd}T12:00", body.TimeZoneId) : clock.GetUtcNow();
            return Results.Ok(new { quote = CampConfigurationRules.Quote(configuration, answers, body.Days,
                body.TimeZoneId, instant, body.NeedsAccommodation) });
        }
        catch (Exception exception) { return MiniAppEndpointSupport.FromException(exception); }
    }
}
