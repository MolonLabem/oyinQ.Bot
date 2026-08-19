namespace oyinQ.Bot.Integrations.Tesera;

public static class TeseraGameUrlParser
{
    public static string? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var value = input.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !(uri.Host.Equals("tesera.ru", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.tesera.ru", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2
            || !segments[0].Equals("game", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var alias = Uri.UnescapeDataString(segments[1]).Trim();
        return alias.Length is > 0 and <= 200 ? alias : null;
    }
}
