using System.Globalization;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public static class BggBestPlayerRecommendation
{
    // Calculate/Collapse emits comma-separated counts and inclusive en-dash ranges.
    // Read historical hyphens/em dashes too, but reject the entire value if any part is malformed.
    public static bool Matches(string? recommendation, int players)
    {
        if (players <= 0 || string.IsNullOrWhiteSpace(recommendation)) return false;
        var matches = false;
        foreach (var part in recommendation.Split(','))
        {
            var bounds = part.Split(['-', '–', '—']);
            if (bounds.Length is < 1 or > 2 || !TryCount(bounds[0], out var minimum)) return false;
            var maximum = minimum;
            if (bounds.Length == 2 && (!TryCount(bounds[1], out maximum) || maximum < minimum)) return false;
            matches |= players >= minimum && players <= maximum;
        }
        return matches;
    }

    private static bool TryCount(string value, out int count) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) && count > 0;
}
