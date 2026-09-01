using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using oyinQ.Bot.Features.Collections;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public sealed class BoardGameGeekClient(
    HttpClient httpClient,
    IOptions<BggOptions> options)
    : IBoardGameGeekClient
{
    private const int CollectionAcceptedAttempts = 5;
    private const int TransientAttempts = 3;
    private const int ThingAttempts = 2;
    private const int ThingBatchSize = 20;
    private static readonly TimeSpan AcceptedRetryDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TransientRetryJitter = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan ThingBatchDelay = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<ExternalGameSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length < 2)
        {
            return [];
        }

        var document = await GetXmlAsync(
            $"/xmlapi2/search?query={Uri.EscapeDataString(normalizedQuery)}&type=boardgame",
            cancellationToken);

        var results = document.Root?
            .Elements("item")
            .Select(item =>
            {
                var id = ReadLongAttribute(item, "id");
                var primaryName = item.Elements("name")
                    .FirstOrDefault(name => string.Equals(
                        (string?)name.Attribute("type"),
                        "primary",
                        StringComparison.OrdinalIgnoreCase));
                var name = (string?)primaryName?.Attribute("value");
                var year = ReadIntValue(item.Element("yearpublished"));

                return id is null || string.IsNullOrWhiteSpace(name)
                    ? null
                    : new ExternalGameSearchResult(id.Value, name, year);
            })
            .Where(result => result is not null)
            .Cast<ExternalGameSearchResult>()
            .ToArray()
            ?? [];
        return RankSearchResults(results, normalizedQuery).Take(25).ToArray();
    }

    public static IReadOnlyList<ExternalGameSearchResult> RankSearchResults(
        IEnumerable<ExternalGameSearchResult> results, string query)
    {
        var normalized = NormalizeSearch(query);
        return results.OrderBy(result => SearchRank(NormalizeSearch(result.Name), normalized))
            .ThenBy(result => result.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(result => result.YearPublished)
            .ToArray();
    }

    private static (int Tier, int Position) SearchRank(string name, string query)
    {
        if (name == query) return (0, 0);
        if (name.StartsWith(query, StringComparison.Ordinal)) return (1, 0);
        var words = Regex.Split(name, @"[^\p{L}\p{N}]+").Where(x => x.Length > 0).ToArray();
        if (words.Any(word => word == query)) return (2, 0);
        var wordPrefix = Array.FindIndex(words, word => word.StartsWith(query, StringComparison.Ordinal));
        if (wordPrefix >= 0) return (3, wordPrefix);
        var position = name.IndexOf(query, StringComparison.Ordinal);
        return position >= 0 ? (4, position) : (5, int.MaxValue);
    }

    private static string NormalizeSearch(string value) => value.Trim().ToLowerInvariant();

    public async Task<BggGameDetails?> GetGameDetailsAsync(
        long bggId,
        CancellationToken cancellationToken)
    {
        if (bggId <= 0)
        {
            return null;
        }

        var document = await GetXmlAsync(
            $"/xmlapi2/thing?id={bggId}&type=boardgame&stats=1",
            cancellationToken,
            acceptedAttempts: ThingAttempts,
            transientAttempts: ThingAttempts);
        var item = document.Root?.Elements("item").SingleOrDefault();
        var game = item is null ? null : ParseThing(item);
        if (game is null)
        {
            return null;
        }

        var expansions = item!.Elements("link")
            .Where(link => string.Equals((string?)link.Attribute("type"), "boardgameexpansion", StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)link.Attribute("inbound"), "true", StringComparison.OrdinalIgnoreCase))
            .Select(link =>
            {
                var id = ReadLongAttribute(link, "id");
                var name = ((string?)link.Attribute("value"))?.Trim();
                return id is null || string.IsNullOrWhiteSpace(name)
                    ? null
                    : new BggExpansion(id.Value, name);
            })
            .Where(value => value is not null)
            .Cast<BggExpansion>()
            .DistinctBy(value => value.BggId)
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new BggGameDetails(game, expansions);
    }

    public async Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(
        string username,
        CancellationToken cancellationToken)
    {
        var collection = await FetchCollectionAsync(username, "boardgame", "boardgameexpansion", cancellationToken);
        if (collection.Count == 0)
        {
            return [];
        }

        var enriched = await FetchThingsAsync(
            collection.Select(item => item.BggId).ToArray(),
            cancellationToken);

        return collection
            .Where(item => enriched.ContainsKey(item.BggId))
            .Select(item => Merge(item, enriched[item.BggId]))
            .ToArray();
    }

    public async Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(
        string username,
        CancellationToken cancellationToken)
    {
        var collection = await FetchCollectionAsync(username, "boardgameexpansion", null, cancellationToken);
        if (collection.Count == 0) return [];

        var result = new List<BggOwnedExpansion>();
        var ids = collection.Select(value => value.BggId).Distinct().ToArray();
        for (var offset = 0; offset < ids.Length; offset += ThingBatchSize)
        {
            if (offset > 0) await Task.Delay(ThingBatchDelay, cancellationToken);
            var batch = ids.Skip(offset).Take(ThingBatchSize).ToArray();
            var document = await GetXmlAsync(
                $"/xmlapi2/thing?id={string.Join(',', batch)}&type=boardgameexpansion&stats=1",
                cancellationToken,
                acceptedAttempts: ThingAttempts,
                transientAttempts: ThingAttempts);
            foreach (var item in document.Root?.Elements("item") ?? [])
            {
                var expansion = ParseThing(item, "boardgameexpansion");
                if (expansion is null) continue;
                var parentIds = item.Elements("link")
                    .Where(link => string.Equals((string?)link.Attribute("type"), "boardgameexpansion", StringComparison.OrdinalIgnoreCase)
                        && string.Equals((string?)link.Attribute("inbound"), "true", StringComparison.OrdinalIgnoreCase))
                    .Select(link => ReadLongAttribute(link, "id"))
                    .Where(value => value is > 0)
                    .Select(value => value!.Value)
                    .Distinct()
                    .ToArray();
                result.Add(new BggOwnedExpansion(expansion, parentIds));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(
        IReadOnlyCollection<long> bggIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bggIds);
        var ids = bggIds.Where(id => id > 0).Distinct().ToArray();
        var result = new List<BggCollectionItem>();
        for (var offset = 0; offset < ids.Length; offset += ThingBatchSize)
        {
            if (offset > 0) await Task.Delay(ThingBatchDelay, cancellationToken);
            var batch = ids.Skip(offset).Take(ThingBatchSize).ToArray();
            var document = await GetXmlAsync(
                $"/xmlapi2/thing?id={string.Join(',', batch)}&stats=1",
                cancellationToken,
                acceptedAttempts: ThingAttempts,
                transientAttempts: ThingAttempts);
            foreach (var item in document.Root?.Elements("item") ?? [])
            {
                var type = ((string?)item.Attribute("type"))?.Trim();
                var isExpansion = string.Equals(type, "boardgameexpansion",
                    StringComparison.OrdinalIgnoreCase);
                if (!isExpansion && !string.Equals(type, "boardgame", StringComparison.OrdinalIgnoreCase))
                    continue;
                var game = ParseThing(item, isExpansion ? "boardgameexpansion" : "boardgame");
                if (game is not null)
                    result.Add(new BggCollectionItem(game, isExpansion,
                        isExpansion ? ReadExpansionParentIds(item) : []));
            }
        }

        return result.OrderByDescending(item => item.IsExpansion)
            .DistinctBy(item => item.Game.BggId).ToArray();
    }

    private async Task<IReadOnlyList<CollectionItem>> FetchCollectionAsync(
        string username,
        string subtype,
        string? excludedSubtype,
        CancellationToken cancellationToken)
    {
        var exclude = string.IsNullOrWhiteSpace(excludedSubtype)
            ? string.Empty
            : $"&excludesubtype={Uri.EscapeDataString(excludedSubtype)}";
        var document = await GetXmlAsync(
            $"/xmlapi2/collection?username={Uri.EscapeDataString(username)}&own=1&subtype={Uri.EscapeDataString(subtype)}{exclude}&stats=1",
            cancellationToken,
            acceptedAttempts: CollectionAcceptedAttempts);

        return document.Root?
            .Elements("item")
            .Select(ParseCollectionItem)
            .Where(item => item is not null)
            .Cast<CollectionItem>()
            .ToArray()
            ?? [];
    }

    private async Task<Dictionary<long, ExternalGame>> FetchThingsAsync(
        IReadOnlyCollection<long> bggIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, ExternalGame>();
        var ids = bggIds.Where(id => id > 0).Distinct().ToArray();

        for (var offset = 0; offset < ids.Length; offset += ThingBatchSize)
        {
            if (offset > 0)
            {
                await Task.Delay(ThingBatchDelay, cancellationToken);
            }

            var batch = ids.Skip(offset).Take(ThingBatchSize).ToArray();
            var document = await GetXmlAsync(
                $"/xmlapi2/thing?id={string.Join(',', batch)}&type=boardgame&stats=1",
                cancellationToken,
                acceptedAttempts: ThingAttempts,
                transientAttempts: ThingAttempts);

            foreach (var item in document.Root?.Elements("item") ?? [])
            {
                var parsed = ParseThing(item);
                if (parsed?.BggId is { } bggId)
                {
                    result[bggId] = parsed;
                }
            }
        }

        return result;
    }

    private async Task<XDocument> GetXmlAsync(
        string relativeUrl,
        CancellationToken cancellationToken,
        int acceptedAttempts = TransientAttempts,
        int transientAttempts = TransientAttempts)
    {
        if (!options.Value.IsAvailable)
        {
            throw new BggUnavailableException("BGG временно отключён: API token не настроен.");
        }

        using var response = await HttpRetryHelper.SendAsync(
            async retryCancellationToken =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    options.Value.ApiToken);

                return await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    retryCancellationToken);
            },
            maxTransientAttempts: transientAttempts,
            maxAcceptedAttempts: acceptedAttempts,
            acceptedRetryDelay: AcceptedRetryDelay,
            transientRetryDelay: TransientRetryDelay,
            maxJitter: TransientRetryJitter,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            throw new HttpRequestException(
                "BGG не успел подготовить ответ после нескольких повторов.",
                null,
                response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        return XDocument.Parse(xml);
    }

    private static CollectionItem? ParseCollectionItem(XElement item)
    {
        var bggId = ReadLongAttribute(item, "objectid");
        var name = item.Element("name")?.Value?.Trim();
        if (bggId is null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var stats = item.Element("stats");
        return new CollectionItem(
            bggId.Value,
            name,
            ReadIntAttribute(stats, "minplayers"),
            ReadIntAttribute(stats, "maxplayers"));
    }

    private static ExternalGame? ParseThing(XElement item, string expectedType = "boardgame")
    {
        if (!string.Equals(
                (string?)item.Attribute("type"),
                expectedType,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bggId = ReadLongAttribute(item, "id");
        var primaryName = item.Elements("name")
            .FirstOrDefault(name => string.Equals(
                (string?)name.Attribute("type"),
                "primary",
                StringComparison.OrdinalIgnoreCase));
        var name = (string?)primaryName?.Attribute("value");

        if (bggId is null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var subdomains = ReadSubdomains(item);
        var categories = ReadTaxonomy(item, "boardgamecategory");
        var mechanics = ReadTaxonomy(item, "boardgamemechanic");
        return new ExternalGame(
            bggId,
            name,
            ReadIntValue(item.Element("minplayers")),
            ReadIntValue(item.Element("maxplayers")),
            BggBestPlayerCalculator.Calculate(item),
            $"https://boardgamegeek.com/boardgame/{bggId.Value}",
            ReadImageUrl(item.Element("thumbnail")),
            ReadImageUrl(item.Element("image")),
            subdomains.Select(value => NormalizeSubdomain(value.Name)).ToArray(),
            categories.Select(value => value.Name).ToArray(),
            NormalizeDescription(item.Element("description")?.Value),
            ReadIntValue(item.Element("yearpublished")),
            ReadIntValue(item.Element("minplaytime")),
            ReadIntValue(item.Element("maxplaytime")),
            ReadIntValue(item.Element("minage")),
            subdomains,
            categories,
            mechanics,
            BggTaxonomyCatalog.MapGameType(subdomains));
    }

    private static IReadOnlyList<string> ReadTags(XElement item, string linkType,
        Func<string, string>? normalize = null) => item.Elements("link")
        .Where(link => string.Equals((string?)link.Attribute("type"), linkType,
            StringComparison.OrdinalIgnoreCase))
        .Select(link => ((string?)link.Attribute("value"))?.Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => normalize is null ? value! : normalize(value!))
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IReadOnlyList<GameTaxonomyItem> ReadTaxonomy(XElement item, string linkType) => item.Elements("link")
        .Where(link => string.Equals((string?)link.Attribute("type"), linkType, StringComparison.OrdinalIgnoreCase))
        .Select(link => new { Id = ReadLongAttribute(link, "id"), Name = ((string?)link.Attribute("value"))?.Trim() })
        .Where(value => value.Id is > 0 && !string.IsNullOrWhiteSpace(value.Name))
        .Select(value => new GameTaxonomyItem(value.Id!.Value, value.Name!))
        .DistinctBy(value => value.BggId)
        .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IReadOnlyList<long> ReadExpansionParentIds(XElement item) => item.Elements("link")
        .Where(link => string.Equals((string?)link.Attribute("type"), "boardgameexpansion",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals((string?)link.Attribute("inbound"), "true",
                StringComparison.OrdinalIgnoreCase))
        .Select(link => ReadLongAttribute(link, "id"))
        .Where(value => value is > 0)
        .Select(value => value!.Value)
        .Distinct()
        .ToArray();

    private static IReadOnlyList<GameTaxonomyItem> ReadSubdomains(XElement item)
    {
        var direct = ReadTaxonomy(item, "boardgamesubdomain");
        var ranked = item.Descendants("rank")
            .Where(rank => string.Equals((string?)rank.Attribute("type"), "family", StringComparison.OrdinalIgnoreCase))
            .Select(rank => ReadLongAttribute(rank, "id"))
            .Where(id => id is > 0 && BggTaxonomyCatalog.IsKnownSubdomain(id.Value))
            .Select(id => new GameTaxonomyItem(id!.Value, BggTaxonomyCatalog.SubdomainName(id.Value)));
        return direct.Concat(ranked).DistinctBy(value => value.BggId)
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string? NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var decoded = WebUtility.HtmlDecode(value);
        decoded = Regex.Replace(decoded, "(?i)<br\\s*/?>", "\n");
        decoded = Regex.Replace(decoded, "(?i)</p\\s*>", "\n\n");
        decoded = Regex.Replace(decoded, "<[^>]+>", string.Empty);
        decoded = decoded.Replace("\r\n", "\n").Replace('\r', '\n');
        decoded = Regex.Replace(decoded, "[\\t ]+", " ");
        decoded = Regex.Replace(decoded, " *\n *", "\n");
        decoded = Regex.Replace(decoded, "\n{3,}", "\n\n").Trim();
        return decoded.Length == 0 ? null : decoded[..Math.Min(decoded.Length, 20_000)];
    }

    private static string NormalizeSubdomain(string value) => value.EndsWith(" Games",
        StringComparison.OrdinalIgnoreCase) ? value[..^6].TrimEnd() : value;

    private static ExternalGame Merge(CollectionItem collectionItem, ExternalGame enriched) =>
        enriched with
        {
            MinPlayers = enriched.MinPlayers ?? collectionItem.MinPlayers,
            MaxPlayers = enriched.MaxPlayers ?? collectionItem.MaxPlayers
        };

    private static int? ReadIntValue(XElement? element)
    {
        var value = (string?)element?.Attribute("value");
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? ReadImageUrl(XElement? element)
    {
        var value = element?.Value.Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http"
                ? uri.AbsoluteUri
                : null;
    }

    private static int? ReadIntAttribute(XElement? element, string attributeName)
    {
        var value = (string?)element?.Attribute(attributeName);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static long? ReadLongAttribute(XElement element, string attributeName)
    {
        var value = (string?)element.Attribute(attributeName);
        return long.TryParse(value, out var parsed) ? parsed : null;
    }

    private sealed record CollectionItem(
        long BggId,
        string Name,
        int? MinPlayers,
        int? MaxPlayers);
}

public sealed class BggUnavailableException(string message) : HttpRequestException(message);
