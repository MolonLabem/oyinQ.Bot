namespace oyinQ.Bot.Integrations.BoardGameGeek;

public static class BggUsernameParser
{
    public static string? Parse(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Any(char.IsWhiteSpace))
        {
            return null;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            if (!value.Contains('/') && !value.Contains('?'))
            {
                return IsValid(value) ? value : null;
            }

            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !(uri.Host.Equals("boardgamegeek.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".boardgamegeek.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("username", out var fromQuery) && IsValid(fromQuery))
        {
            return fromQuery;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("user", StringComparison.OrdinalIgnoreCase)
                && IsValid(segments[index + 1]))
            {
                return segments[index + 1];
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2)
            {
                values[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1].Replace('+', ' '));
            }
        }

        return values;
    }

    private static bool IsValid(string value) =>
        value.Length is > 0 and <= 100
        && !value.Any(char.IsWhiteSpace)
        && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');
}
