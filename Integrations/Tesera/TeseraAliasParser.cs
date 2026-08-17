namespace oyinQ.Bot.Integrations.Tesera;

public static class TeseraAliasParser
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
            if (!value.Contains('/'))
            {
                var decoded = Uri.UnescapeDataString(value);
                return IsValid(decoded) ? decoded : null;
            }

            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !(uri.Host.Equals("tesera.ru", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".tesera.ru", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if ((segments[index].Equals("user", StringComparison.OrdinalIgnoreCase)
                    || segments[index].Equals("users", StringComparison.OrdinalIgnoreCase))
                && IsValid(segments[index + 1]))
            {
                return segments[index + 1];
            }
        }

        return null;
    }

    private static bool IsValid(string value) =>
        value.Length is > 0 and <= 100
        && !value.Any(char.IsWhiteSpace)
        && !value.Contains('/');
}
