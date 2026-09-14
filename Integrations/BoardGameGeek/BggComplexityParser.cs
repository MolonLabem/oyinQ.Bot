using System.Globalization;
using System.Xml.Linq;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

internal static class BggComplexityParser
{
    public static (decimal? Weight, GameComplexity? Level) Parse(XElement item)
    {
        var raw = (string?)item.Element("statistics")?.Element("ratings")?.Element("averageweight")?.Attribute("value");
        var weight = decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? GameComplexityPresentation.ValidWeight(parsed) : null;
        var votes = item.Elements("poll").Where(x => (string?)x.Attribute("name") == "boardgameweight")
            .Descendants("result").Select(x => new
            {
                Level = ReadLevel((string?)x.Attribute("value")),
                Votes = long.TryParse((string?)x.Attribute("numvotes"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var count) ? count : 0
            }).Where(x => x.Level is not null && x.Votes > 0)
            .GroupBy(x => x.Level!.Value)
            .Select(group => new { Level = group.Key, Votes = group.Sum(x => (decimal)x.Votes) })
            .OrderByDescending(x => x.Votes)
            .ThenBy(x => weight is { } average ? Math.Abs((decimal)x.Level - average) : 0)
            // Equal distance (or no average) prefers the lighter category, independently of XML order.
            .ThenBy(x => x.Level).FirstOrDefault();
        return (weight, votes?.Level ?? GameComplexityPresentation.Resolve(weight, null));
    }

    private static GameComplexity? ReadLevel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "1" or "light" => GameComplexity.Light,
        "2" or "medium light" => GameComplexity.MediumLight,
        "3" or "medium" => GameComplexity.Medium,
        "4" or "medium heavy" => GameComplexity.MediumHeavy,
        "5" or "heavy" => GameComplexity.Heavy,
        _ => null
    };
}
