namespace oyinQ.Bot.Common.Options;

public sealed record TeseraOptions(Uri BaseAddress, string? ProxySecret)
{
    private static readonly Uri DirectBaseAddress = new("https://api.tesera.ru");

    public bool UsesProxy => ProxySecret is not null;

    public static TeseraOptions FromConfiguration(IConfiguration configuration)
    {
        var proxyBaseUrl = configuration["TESERA_PROXY_BASE_URL"]?.Trim();
        var proxySecret = configuration["TESERA_PROXY_SECRET"]?.Trim();

        if (string.IsNullOrWhiteSpace(proxyBaseUrl) && string.IsNullOrWhiteSpace(proxySecret))
        {
            return new TeseraOptions(DirectBaseAddress, null);
        }

        if (string.IsNullOrWhiteSpace(proxyBaseUrl) || string.IsNullOrWhiteSpace(proxySecret))
        {
            throw new InvalidOperationException(
                "TESERA_PROXY_BASE_URL and TESERA_PROXY_SECRET must be configured together.");
        }

        if (!Uri.TryCreate(proxyBaseUrl, UriKind.Absolute, out var baseAddress)
            || (baseAddress.Scheme != Uri.UriSchemeHttps
                && !(baseAddress.Scheme == Uri.UriSchemeHttp && baseAddress.IsLoopback))
            || baseAddress.Host.Equals(DirectBaseAddress.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "TESERA_PROXY_BASE_URL must be an absolute Worker HTTPS URL (HTTP is allowed only for loopback development).");
        }

        return new TeseraOptions(baseAddress, proxySecret);
    }
}
