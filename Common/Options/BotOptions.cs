using System.Text.RegularExpressions;

namespace oyinQ.Bot.Common.Options;

public sealed partial class BotOptions
{
    public string Token { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
    public string PublicBaseUrl { get; init; } = string.Empty;
    public bool UseLongPolling { get; init; }

    public static BotOptions FromConfiguration(IConfiguration configuration)
    {
        var token = configuration["TELEGRAM_BOT_TOKEN"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("TELEGRAM_BOT_TOKEN is required.");
        }

        var useLongPolling = bool.TryParse(configuration["USE_LONG_POLLING"], out var parsed)
            && parsed;

        var webhookSecret = configuration["TELEGRAM_WEBHOOK_SECRET"]?.Trim() ?? string.Empty;
        var publicBaseUrl = configuration["PUBLIC_BASE_URL"]?.Trim() ?? string.Empty;

        if (!useLongPolling)
        {
            if (!WebhookSecretPattern().IsMatch(webhookSecret))
            {
                throw new InvalidOperationException(
                    "TELEGRAM_WEBHOOK_SECRET is required in webhook mode and must contain 1-256 A-Z, a-z, 0-9, _ or - characters.");
            }

            if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    "PUBLIC_BASE_URL is required in webhook mode and must be an absolute HTTPS URL.");
            }

        }

        return new BotOptions
        {
            Token = token,
            WebhookSecret = webhookSecret,
            PublicBaseUrl = publicBaseUrl,
            UseLongPolling = useLongPolling
        };
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookSecretPattern();
}
