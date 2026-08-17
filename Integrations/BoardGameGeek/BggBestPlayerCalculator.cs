using System.Xml.Linq;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public static class BggBestPlayerCalculator
{
    public static string? Calculate(XElement item)
    {
        var poll = item.Elements("poll")
            .FirstOrDefault(value => string.Equals(
                (string?)value.Attribute("name"),
                "suggested_numplayers",
                StringComparison.OrdinalIgnoreCase));

        if (poll is null)
        {
            return null;
        }

        var selected = new List<string>();
        foreach (var results in poll.Elements("results"))
        {
            var playerCount = (string?)results.Attribute("numplayers");
            if (string.IsNullOrWhiteSpace(playerCount))
            {
                continue;
            }

            var votes = results.Elements("result")
                .ToDictionary(
                    result => (string?)result.Attribute("value") ?? string.Empty,
                    result => int.TryParse((string?)result.Attribute("numvotes"), out var count) ? count : 0,
                    StringComparer.OrdinalIgnoreCase);

            votes.TryGetValue("Best", out var best);
            votes.TryGetValue("Recommended", out var recommended);
            votes.TryGetValue("Not Recommended", out var notRecommended);

            if (best > 0 && best >= recommended && best >= notRecommended)
            {
                selected.Add(playerCount.Trim());
            }
        }

        return Collapse(selected);
    }

    internal static string? Collapse(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var numeric = values
            .Select(value => int.TryParse(value, out var count) ? (int?)count : null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .Order()
            .ToArray();

        var parts = new List<string>();
        for (var index = 0; index < numeric.Length;)
        {
            var start = numeric[index];
            var end = start;
            while (index + 1 < numeric.Length && numeric[index + 1] == end + 1)
            {
                index++;
                end = numeric[index];
            }

            parts.Add(start == end ? start.ToString() : $"{start}–{end}");
            index++;
        }

        parts.AddRange(values
            .Where(value => !int.TryParse(value, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
