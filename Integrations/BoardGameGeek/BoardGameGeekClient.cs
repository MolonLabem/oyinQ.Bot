using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public sealed class BoardGameGeekClient(
    HttpClient httpClient,
    IOptions<BggOptions> options,
    ILogger<BoardGameGeekClient> logger)
    : IBoardGameGeekClient
{
    private const int CollectionAcceptedAttempts = 5;
    private const int TransientAttempts = 3;
    private const int ThingBatchSize = 100;
    private static readonly TimeSpan AcceptedRetryDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ThingBatchDelay = TimeSpan.FromMilliseconds(120);

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

    public async Task<IReadOnlyList<ExternalGame>> GetOwnedCollectionAsync(
        string username,
        CancellationToken cancellationToken)
    {
        var collection = await FetchCollectionAsync(username, cancellationToken);
        if (collection.Count == 0)
        {
            return [];
        }

        var enriched = await FetchThingsAsync(
            collection.Select(item => item.BggId).ToArray(),
            cancellationToken);

        return collection
            .Select(item => Merge(item, enriched.GetValueOrDefault(item.BggId)))
            .ToArray();
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

        var collection = await FetchCollectionAsync(username, cancellationToken);
        var slice = collection.Skip(offset).Take(limit).ToArray();
        if (slice.Length == 0)
        {
            return new ExternalCollectionStep([], Math.Min(offset, collection.Count), collection.Count);
        }

        var enriched = await FetchThingsAsync(
            slice.Select(item => item.BggId).ToArray(),
            cancellationToken);
        var games = slice
            .Select(item => Merge(item, enriched.GetValueOrDefault(item.BggId)))
            .ToArray();

        return new ExternalCollectionStep(
            games,
            Math.Min(offset + slice.Length, collection.Count),
            collection.Count);
    }

    private async Task<IReadOnlyList<CollectionItem>> FetchCollectionAsync(
        string username,
        CancellationToken cancellationToken)
    {
        var document = await GetXmlAsync(
            $"/xmlapi2/collection?username={Uri.EscapeDataString(username)}&own=1&excludesubtype=boardgameexpansion&stats=1",
            cancellationToken,
            CollectionAcceptedAttempts);

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
                $"/xmlapi2/thing?id={string.Join(',', batch)}&stats=1",
                cancellationToken);

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
        int acceptedAttempts = TransientAttempts)
    {
        var transientAttempt = 0;
        var acceptedAttempt = 0;

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiToken);

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                acceptedAttempt++;
                if (acceptedAttempt >= acceptedAttempts)
                {
                    throw new HttpRequestException(
                        "BGG не успел подготовить коллекцию после нескольких повторов.",
                        null,
                        response.StatusCode);
                }

                logger.LogDebug(
                    "BGG returned HTTP 202 for {Url}; retry {Attempt}/{MaxAttempts}.",
                    relativeUrl,
                    acceptedAttempt,
                    acceptedAttempts);
                await Task.Delay(AcceptedRetryDelay, cancellationToken);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                || (int)response.StatusCode == 429)
            {
                transientAttempt++;
                if (transientAttempt < TransientAttempts)
                {
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 350));
                    await Task.Delay(TransientRetryDelay + jitter, cancellationToken);
                    continue;
                }
            }

            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return XDocument.Parse(xml);
        }
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

    private static ExternalGame? ParseThing(XElement item)
    {
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
            null,
            name,
            ReadIntValue(item.Element("minplayers")),
            ReadIntValue(item.Element("maxplayers")),
            BggBestPlayerCalculator.Calculate(item),
            $"https://boardgamegeek.com/boardgame/{bggId.Value}");
    }

    private static ExternalGame Merge(CollectionItem collectionItem, ExternalGame? enriched) =>
        enriched is null
            ? new ExternalGame(
                collectionItem.BggId,
                null,
                collectionItem.Name,
                collectionItem.MinPlayers,
                collectionItem.MaxPlayers,
                null,
                $"https://boardgamegeek.com/boardgame/{collectionItem.BggId}")
            : enriched with
            {
                MinPlayers = enriched.MinPlayers ?? collectionItem.MinPlayers,
                MaxPlayers = enriched.MaxPlayers ?? collectionItem.MaxPlayers
            };

    private static int? ReadIntValue(XElement? element)
    {
        var value = (string?)element?.Attribute("value");
        return int.TryParse(value, out var parsed) ? parsed : null;
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
