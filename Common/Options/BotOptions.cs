using System.Text.RegularExpressions;

namespace oyinQ.Bot.Common.Options;

public sealed partial class BotOptions
{
    public const string SectionName = "Telegram";

    public string Token { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
    public string PublicBaseUrl { get; init; } = string.Empty;
    public bool UseLongPolling { get; init; }

    public static BotOptions FromConfiguration(IConfiguration configuration)
    {
        var token = configuration[$"{SectionName}:Token"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Telegram:Token is required.");
        }

        var useLongPolling = bool.TryParse(configuration[$"{SectionName}:UseLongPolling"], out var parsed)
            && parsed;

        var webhookSecret = configuration[$"{SectionName}:WebhookSecret"]?.Trim() ?? string.Empty;
        var publicBaseUrl = configuration[$"{SectionName}:PublicBaseUrl"]?.Trim() ?? string.Empty;

        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Telegram:PublicBaseUrl is required and must be an absolute HTTPS URL.");
        }

        if (!useLongPolling)
        {
            if (!WebhookSecretPattern().IsMatch(webhookSecret))
            {
                throw new InvalidOperationException(
                    "Telegram:WebhookSecret is required in webhook mode and must contain 1-256 A-Z, a-z, 0-9, _ or - characters.");
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
