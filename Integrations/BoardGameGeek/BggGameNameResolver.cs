using System.Xml.Linq;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public sealed record BggResolvedName(string DisplayName, string OriginalName, string? RussianName);

public static class BggGameNameResolver
{
    // Stable BGG language identifier returned by XML API2 version records.
    private const long RussianLanguageId = 2202;

    public static BggResolvedName? Resolve(XElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var originalName = item.Elements("name")
            .FirstOrDefault(name => string.Equals((string?)name.Attribute("type"), "primary",
                StringComparison.OrdinalIgnoreCase))?
            .Attribute("value")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 300) return null;

        var russianNames = (item.Element("versions")?.Elements("item") ?? [])
            .Where(IsRussianOnlyVersion)
            .Select(ReadCanonicalName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var russianName = russianNames.Length == 1 ? russianNames[0] : null;
        return new BggResolvedName(russianName ?? originalName, originalName, russianName);
    }

    private static bool IsRussianOnlyVersion(XElement version)
    {
        var languages = version.Elements("link")
            .Where(link => string.Equals((string?)link.Attribute("type"), "language",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return languages.Length == 1 && IsRussian(languages[0]);
    }

    private static bool IsRussian(XElement language) =>
        long.TryParse((string?)language.Attribute("id"), out var id) && id == RussianLanguageId
        || string.Equals(((string?)language.Attribute("value"))?.Trim(), "Russian",
            StringComparison.OrdinalIgnoreCase);

    private static string? ReadCanonicalName(XElement version)
    {
        var canonicalName = ((string?)version.Element("canonicalname")?.Attribute("value"))?.Trim();
        if (string.IsNullOrWhiteSpace(canonicalName) || canonicalName.Length > 300) return null;
        var nickname = version.Elements("name")
            .FirstOrDefault(name => string.Equals((string?)name.Attribute("type"), "primary",
                StringComparison.OrdinalIgnoreCase))?
            .Attribute("value")?.Value.Trim();
        if (string.Equals(canonicalName, nickname, StringComparison.OrdinalIgnoreCase)
            || string.Equals(canonicalName, "Russian edition", StringComparison.OrdinalIgnoreCase))
            return null;
        return canonicalName;
    }
}
