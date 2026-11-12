using System.Globalization;
using System.Xml.Linq;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public interface IBggRatingSource
{
    Task<IReadOnlyDictionary<long, decimal?>> GetRatingsAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
}

public static class BggRatingParser
{
    public static bool TryRead(XElement item, out decimal? rating)
    {
        rating = null;
        var ratings = item.Element("statistics")?.Element("ratings");
        if (!int.TryParse((string?)ratings?.Element("usersrated")?.Attribute("value"), NumberStyles.None,
                CultureInfo.InvariantCulture, out var votes) || votes < 0) return false;
        if (votes == 0) return true;
        if (!decimal.TryParse((string?)ratings?.Element("average")?.Attribute("value"), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var average) || average is < 1 or > 10) return false;
        rating = average;
        return true;
    }
}
