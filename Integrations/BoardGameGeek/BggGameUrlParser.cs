using System.Text.RegularExpressions;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public static partial class BggGameUrlParser
{
    public static long? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = GameUrlRegex().Match(value.Trim());
        return match.Success
            && long.TryParse(match.Groups[1].Value, out var id)
            && id > 0
                ? id
                : null;
    }

    [GeneratedRegex(
        @"^(?:https?://)?(?:www\.)?boardgamegeek\.com/boardgame/(\d+)(?:/[^\s?#]*)?(?:[?#][^\s]*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GameUrlRegex();
}
