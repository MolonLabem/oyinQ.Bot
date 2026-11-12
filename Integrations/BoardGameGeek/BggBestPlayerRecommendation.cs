using System.Globalization;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public static class BggBestPlayerRecommendation
{
    // Calculate/Collapse emits comma-separated counts and inclusive en-dash ranges.
    // Read historical hyphens/em dashes too, but reject the entire value if any part is malformed.
    public static bool Matches(string? recommendation, int players) => Matches(recommendation, players, players);

    public static bool Matches(string? recommendation, int? from, int? to) =>
        from is not <= 0 && to is not <= 0 && !(from > to)
        && Ranges(recommendation).Any(range => !(from > range.Maximum) && !(to < range.Minimum));

    public static IReadOnlyList<(int Minimum, int Maximum)> Ranges(string? recommendation)
    {
        if (string.IsNullOrWhiteSpace(recommendation)) return [];
        var ranges = new List<(int Minimum, int Maximum)>();
        foreach (var part in recommendation.Split(','))
        {
            var bounds = part.Split(['-', '–', '—']);
            if (bounds.Length is < 1 or > 2 || !TryCount(bounds[0], out var minimum)) return [];
            var maximum = minimum;
            if (bounds.Length == 2 && (!TryCount(bounds[1], out maximum) || maximum < minimum)) return [];
            ranges.Add((minimum, maximum));
        }
        return ranges;
    }

    private static bool TryCount(string value, out int count) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) && count > 0;
}
