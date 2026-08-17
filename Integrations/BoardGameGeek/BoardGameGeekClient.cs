using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public sealed class BoardGameGeekClient(
    HttpClient httpClient,
    IOptions<BggOptions> options,
    ILogger<BoardGameGeekClient> logger)
    : IBoardGameGeekClient
{
    private const int MaxAttempts = 4;
    private static readonly TimeSpan AcceptedRetryDelay = TimeSpan.FromMilliseconds(1500);

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

        var document = await GetXmlAsync(
            $"/xmlapi2/thing?id={bggId}&stats=1",
            cancellationToken);

        var item = document.Root?.Elements("item").FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        var primaryName = item.Elements("name")
            .FirstOrDefault(name => string.Equals(
                (string?)name.Attribute("type"),
                "primary",
                StringComparison.OrdinalIgnoreCase));
        var name = (string?)primaryName?.Attribute("value");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new ExternalGame(
            BggId: bggId,
            TeseraAlias: null,
            Name: name,
            MinPlayers: ReadIntValue(item.Element("minplayers")),
            MaxPlayers: ReadIntValue(item.Element("maxplayers")),
            BestPlayers: CalculateBestPlayers(item),
            ExternalUrl: $"https://boardgamegeek.com/boardgame/{bggId}");
    }

    private async Task<XDocument> GetXmlAsync(
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
            if (!string.IsNullOrWhiteSpace(options.Value.ApiToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    options.Value.ApiToken);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                if (attempt == MaxAttempts)
                {
                    throw new HttpRequestException(
                        "BGG did not finish preparing the response after all retries.");
                }

                logger.LogDebug(
                    "BGG returned HTTP 202 for {Url}; retry {Attempt}/{MaxAttempts}.",
                    relativeUrl,
                    attempt,
                    MaxAttempts);
                await Task.Delay(AcceptedRetryDelay, cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return XDocument.Parse(xml);
        }

        throw new HttpRequestException("BGG did not return a completed response.");
    }

    private static int? ReadIntValue(XElement? element)
    {
        var value = (string?)element?.Attribute("value");
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static long? ReadLongAttribute(XElement element, string attributeName)
    {
        var value = (string?)element.Attribute(attributeName);
        return long.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? CalculateBestPlayers(XElement item)
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

        var best = new List<string>();
        foreach (var results in poll.Elements("results"))
        {
            var playerCount = (string?)results.Attribute("numplayers");
            if (string.IsNullOrWhiteSpace(playerCount))
            {
                continue;
            }

            var votes = results.Elements("result")
                .Select(result => new
                {
                    Value = (string?)result.Attribute("value"),
                    Votes = int.TryParse((string?)result.Attribute("numvotes"), out var count)
                        ? count
                        : 0
                })
                .ToArray();

            var bestVotes = votes.FirstOrDefault(vote => vote.Value == "Best")?.Votes ?? 0;
            var recommendedVotes = votes.FirstOrDefault(vote => vote.Value == "Recommended")?.Votes ?? 0;
            var notRecommendedVotes = votes.FirstOrDefault(vote => vote.Value == "Not Recommended")?.Votes ?? 0;

            if (bestVotes > 0
                && bestVotes >= recommendedVotes
                && bestVotes >= notRecommendedVotes)
            {
                best.Add(playerCount);
            }
        }

        return best.Count == 0 ? null : string.Join(", ", best);
    }
}
