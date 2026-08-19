namespace oyinQ.Bot.Features.Games;

public static class GameExternalLinkLabel
{
    public static string ForUrl(string? externalUrl)
    {
        if (!Uri.TryCreate(externalUrl, UriKind.Absolute, out var uri))
        {
            return "🔗 Открыть страницу игры";
        }

        if (IsHost(uri.Host, "boardgamegeek.com"))
        {
            return "🔗 Открыть BGG";
        }

        if (IsHost(uri.Host, "tesera.ru"))
        {
            return "🔗 Открыть Tesera";
        }

        return "🔗 Открыть страницу игры";
    }

    private static bool IsHost(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);
}
