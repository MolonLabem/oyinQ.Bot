using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
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

        return document.Root?
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
            .Take(5)
            .ToArray()
            ?? [];
    }

    public async Task<ExternalGame?> GetGameAsync(
        long bggId,
        CancellationToken cancellationToken)
    {
        if (bggId <= 0)
        {
            return null;
        }

        var games = await FetchThingsAsync([bggId], cancellationToken);
        return games.GetValueOrDefault(bggId);
    }

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

    public async Task<IReadOnlyList<ExternalGame>> GetOwnedCollectionAsync(
        string username,
        CancellationToken cancellationToken)
        => await GetOwnedBaseGamesAsync(username, cancellationToken);

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

    public async Task<ExternalCollectionStep> GetOwnedCollectionStepAsync(
        string username,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var collection = await FetchCollectionAsync(username, "boardgame", "boardgameexpansion", cancellationToken);
        var slice = collection.Skip(offset).Take(limit).ToArray();
        if (slice.Length == 0)
        {
            return new ExternalCollectionStep([], Math.Min(offset, collection.Count), collection.Count);
        }

        var enriched = await FetchThingsAsync(
            slice.Select(item => item.BggId).ToArray(),
            cancellationToken);
        var games = slice
            .Where(item => enriched.ContainsKey(item.BggId))
            .Select(item => Merge(item, enriched[item.BggId]))
            .ToArray();

        return new ExternalCollectionStep(
            games,
            Math.Min(offset + slice.Length, collection.Count),
            collection.Count);
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

        return new ExternalGame(
            bggId,
            name,
            ReadIntValue(item.Element("minplayers")),
            ReadIntValue(item.Element("maxplayers")),
            BggBestPlayerCalculator.Calculate(item),
            $"https://boardgamegeek.com/boardgame/{bggId.Value}",
            ReadImageUrl(item.Element("thumbnail")),
            ReadImageUrl(item.Element("image")));
    }

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
