using System.Net;
using System.Text.Json;
using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Integrations.Tesera;

public sealed class TeseraClient(
    HttpClient httpClient,
    ILogger<TeseraClient> logger)
    : ITeseraClient
{
    private const int PageSize = 100;
    private const int MaxPages = 200;
    private const int DetailBatchSize = 12;
    private static readonly TimeSpan DetailBatchDelay = TimeSpan.FromMilliseconds(120);
    private static readonly string[] CollectionPaths =
    [
        "/collections/base/own/{0}",
        "/collections/base/Own/{0}",
        "/collections/own/{0}",
        "/collections/Own/{0}"
    ];

    public async Task<IReadOnlyList<ExternalGame>> GetOwnedCollectionAsync(
        string username,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        string? selectedPath = null;
        IReadOnlyList<JsonElement> firstPage = [];
        string? firstSuccessfulEmptyPath = null;
        IReadOnlyList<JsonElement> firstSuccessfulEmptyPage = [];
        var failures = new List<HttpStatusCode>();

        foreach (var pathTemplate in CollectionPaths)
        {
            var path = string.Format(pathTemplate, Uri.EscapeDataString(username));
            try
            {
                var page = await FetchCollectionPageAsync(path, 0, cancellationToken);
                if (page.IsSuccess)
                {
                    if (page.Items.Count > 0)
                    {
                        selectedPath = path;
                        firstPage = page.Items;
                        break;
                    }

                    if (firstSuccessfulEmptyPath is null)
                    {
                        firstSuccessfulEmptyPath = path;
                        firstSuccessfulEmptyPage = page.Items;
                    }

                    continue;
                }

                if (page.StatusCode is { } statusCode)
                {
                    failures.Add(statusCode);
                }
            }
            catch (HttpRequestException exception)
            {
                logger.LogDebug(exception, "Tesera collection endpoint {Path} failed.", path);
            }
        }

        if (selectedPath is null && firstSuccessfulEmptyPath is not null)
        {
            selectedPath = firstSuccessfulEmptyPath;
            firstPage = firstSuccessfulEmptyPage;
        }

        if (selectedPath is null)
        {
            var statuses = failures.Count == 0
                ? "нет ответа"
                : string.Join(", ", failures.Select(status => ((int)status).ToString()).Distinct());
            throw new TeseraUnavailableException(
                $"Tesera API временно недоступен ({statuses}). Попробуйте импорт BGG или повторите позже.");
        }

        var collectionItems = new List<JsonElement>(firstPage);
        var pageItems = firstPage;
        for (var page = 1; page < MaxPages && pageItems.Count == PageSize; page++)
        {
            var response = await FetchCollectionPageAsync(
                selectedPath,
                page * PageSize,
                cancellationToken);
            if (!response.IsSuccess)
            {
                throw new TeseraUnavailableException(
                    "Tesera перестала отвечать во время загрузки коллекции. Попробуйте позже.");
            }

            pageItems = response.Items;
            collectionItems.AddRange(pageItems);
        }

        var aliases = collectionItems
            .Where(item => !IsAddition(item))
            .Select(ExtractAlias)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (aliases.Length == 0)
        {
            return [];
        }

        var games = new List<ExternalGame>(aliases.Length);
        for (var offset = 0; offset < aliases.Length; offset += DetailBatchSize)
        {
            if (offset > 0)
            {
                await Task.Delay(DetailBatchDelay, cancellationToken);
            }

            var batch = aliases.Skip(offset).Take(DetailBatchSize).ToArray();
            var tasks = batch.Select(alias => GetGameByAliasWithRetryAsync(alias, cancellationToken));
            var details = await Task.WhenAll(tasks);
            games.AddRange(details.Where(game => game is not null).Cast<ExternalGame>());
        }

        if (games.Count == 0 && aliases.Length > 0)
        {
            throw new TeseraUnavailableException(
                "Tesera вернула коллекцию, но не отдала данные игр. Попробуйте позже.");
        }

        return games;
    }

    public Task<ExternalGame?> GetGameByAliasAsync(
        string alias,
        CancellationToken cancellationToken) =>
        GetGameByAliasWithRetryAsync(alias, cancellationToken);

    private async Task<ExternalGame?> GetGameByAliasWithRetryAsync(
        string alias,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                var response = await GetJsonAsync(
                    $"/games/{Uri.EscapeDataString(alias)}",
                    cancellationToken);
                if (!response.IsSuccess || response.Document is null)
                {
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        throw new TeseraUnavailableException(
                            $"Tesera API отклонил запрос ({(int)response.StatusCode.Value}).");
                    }

                    if (attempt == 2)
                    {
                        return null;
                    }

                    continue;
                }

                using (response.Document)
                {
                    return ParseGame(response.Document.RootElement, alias);
                }
            }
            catch (TeseraUnavailableException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                if (attempt < 2)
                {
                    logger.LogDebug(exception, "Retrying Tesera game {Alias}.", alias);
                    continue;
                }

                logger.LogWarning(exception, "Tesera game {Alias} failed after retry.", alias);
                return null;
            }
        }

        return null;
    }

    private async Task<CollectionPage> FetchCollectionPageAsync(
        string basePath,
        int offset,
        CancellationToken cancellationToken)
    {
        var separator = basePath.Contains('?') ? '&' : '?';
        var response = await GetJsonAsync(
            $"{basePath}{separator}GamesType=SelfGame&Limit={PageSize}&Offset={offset}",
            cancellationToken);

        if (!response.IsSuccess || response.Document is null)
        {
            return new CollectionPage(false, response.StatusCode, []);
        }

        using (response.Document)
        {
            return new CollectionPage(
                true,
                response.StatusCode,
                ExtractArray(response.Document.RootElement)
                    .Select(element => element.Clone())
                    .ToArray());
        }
    }

    private async Task<JsonResponse> GetJsonAsync(
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new JsonResponse(false, response.StatusCode, null);
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return new JsonResponse(true, response.StatusCode, document);
    }

    private static ExternalGame? ParseGame(JsonElement root, string fallbackAlias)
    {
        var game = root;
        if (root.ValueKind == JsonValueKind.Object
            && (TryGetProperty(root, "game", out var nested)
                || TryGetProperty(root, "Game", out nested)))
        {
            game = nested;
        }

        if (game.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var alias = ReadString(game, "alias")
            ?? ReadString(game, "Alias")
            ?? fallbackAlias;
        var name = ReadFirstString(
            game,
            "titleRus",
            "TitleRus",
            "title",
            "Title",
            "name",
            "Name",
            "titleOriginal",
            "TitleOriginal");

        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var minRecommend = ReadFirstInt(game, "playersMinRecommend", "PlayersMinRecommend");
        var maxRecommend = ReadFirstInt(game, "playersMaxRecommend", "PlayersMaxRecommend");

        return new ExternalGame(
            null,
            alias,
            name,
            ReadFirstInt(game, "playersMin", "PlayersMin"),
            ReadFirstInt(game, "playersMax", "PlayersMax"),
            FormatBestPlayers(minRecommend, maxRecommend),
            $"https://tesera.ru/game/{Uri.EscapeDataString(alias)}");
    }

    private static IReadOnlyList<JsonElement> ExtractArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToArray();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var propertyName in new[] { "games", "items", "data", "results", "rows", "collection", "list" })
        {
            if (TryGetProperty(root, propertyName, out var value))
            {
                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray().ToArray();
                }

                if (value.ValueKind == JsonValueKind.Object)
                {
                    var nested = ExtractArray(value);
                    if (nested.Count > 0)
                    {
                        return nested;
                    }
                }
            }
        }

        return [];
    }

    private static bool IsAddition(JsonElement item)
    {
        var directValue = ReadBool(item, "isAddition");
        if (directValue.HasValue)
        {
            return directValue.Value;
        }

        if (item.ValueKind == JsonValueKind.Object
            && (TryGetProperty(item, "game", out var game)
                || TryGetProperty(item, "Game", out game)))
        {
            return ReadBool(game, "isAddition") is true;
        }

        return false;
    }

    private static string? ExtractAlias(JsonElement item)
    {
        var alias = ReadString(item, "alias") ?? ReadString(item, "Alias");
        if (!string.IsNullOrWhiteSpace(alias))
        {
            return alias;
        }

        if (item.ValueKind == JsonValueKind.Object
            && (TryGetProperty(item, "game", out var game)
                || TryGetProperty(item, "Game", out game)))
        {
            return ReadString(game, "alias") ?? ReadString(game, "Alias");
        }

        return null;
    }

    private static string? FormatBestPlayers(int? min, int? max)
    {
        if (min is null || max is null || min <= 0 || max <= 0)
        {
            return null;
        }

        return min == max ? min.Value.ToString() : $"{min}–{max}";
    }

    private static string? ReadFirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadString(element, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static int? ReadFirstInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadInt(element, name);
            if (value.HasValue)
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        return property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), out number)
            ? number
            : null;
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var number)
            && number is 0 or 1)
        {
            return number == 1;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();
            if (bool.TryParse(text, out var boolean))
            {
                return boolean;
            }

            if (int.TryParse(text, out number) && number is 0 or 1)
            {
                return number == 1;
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private sealed record JsonResponse(
        bool IsSuccess,
        HttpStatusCode? StatusCode,
        JsonDocument? Document);

    private sealed record CollectionPage(
        bool IsSuccess,
        HttpStatusCode? StatusCode,
        IReadOnlyList<JsonElement> Items);
}
