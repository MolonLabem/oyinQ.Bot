using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public sealed record ClubCollectionSourceIds(IReadOnlySet<long> BggIds, int InvalidEntries);

public static class ClubCollectionRefreshGenerator
{
    private static readonly string[] SourceNames = ["club", "guests", "john", "sergei"];
    private const string DefaultSourceBaseUrl =
        "https://raw.githubusercontent.com/BarinDwalin/board-games-club/main/public/data/collections/";

    public static async Task<int> RunAsync(IConfiguration configuration, string[] args,
        CancellationToken cancellationToken = default)
    {
        var options = BggOptions.FromConfiguration(configuration);
        if (!options.IsAvailable)
            throw new InvalidOperationException("BoardGameGeek__ApiToken is required to refresh the Club snapshot.");
        var output = Argument(args, "--output=") ?? Path.Combine("Data", "Imports", "RollMove",
            "club-collection.v2.json");
        var sourceBaseUrl = Argument(args, "--source-base-url=") ?? DefaultSourceBaseUrl;
        if (!sourceBaseUrl.EndsWith('/')) sourceBaseUrl += '/';

        using var sourceClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        sourceClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OyinQ", "1.0"));
        var ids = new HashSet<long>();
        var invalidEntries = 0;
        foreach (var sourceName in SourceNames)
        {
            var json = await sourceClient.GetStringAsync($"{sourceBaseUrl}{sourceName}.json", cancellationToken);
            var source = ExtractBggIds(json);
            ids.UnionWith(source.BggIds);
            invalidEntries += source.InvalidEntries;
        }

        using var bggHttpClient = new HttpClient
        {
            BaseAddress = new Uri("https://boardgamegeek.com"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        var bggClient = new BoardGameGeekClient(bggHttpClient, Options.Create(options));
        var items = await bggClient.GetItemsByIdsAsync(ids, cancellationToken);
        var document = BuildDocument(items, out var orphanExpansions);
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(output, ClubCollectionSerializer.Serialize(document) + Environment.NewLine,
            cancellationToken);

        Console.WriteLine($"Read {ids.Count} unique positive BGG IDs from club, guests, john, and sergei; "
            + $"skipped {invalidEntries} malformed or missing IDs.");
        Console.WriteLine($"BGG resolved {items.Count} canonical items. Excluded {orphanExpansions} expansions "
            + "without an included official parent.");
        var writtenIds = document.Games.Select(game => game.BggId)
            .Concat(document.Games.SelectMany(game => game.Expansions).Select(expansion => expansion.BggId))
            .ToHashSet();
        var omittedSourceIds = ids.Where(id => !writtenIds.Contains(id)).Order().ToArray();
        if (omittedSourceIds.Length > 0)
            Console.WriteLine($"Source IDs not written because BGG did not resolve them as included items: "
                + string.Join(", ", omittedSourceIds) + ".");
        Console.WriteLine($"Wrote {document.Games.Count} base games and "
            + $"{document.Games.SelectMany(game => game.Expansions).Count()} expansion links to {output}.");
        return 0;
    }

    public static ClubCollectionSourceIds ExtractBggIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        var ids = new HashSet<long>();
        var invalidEntries = 0;
        Visit(document.RootElement);
        return new ClubCollectionSourceIds(ids, invalidEntries);

        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "game", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        var game = property.Value;
                        var bggIdProperty = game.EnumerateObject().FirstOrDefault(value =>
                            string.Equals(value.Name, "bggId", StringComparison.OrdinalIgnoreCase));
                        if (!TryReadPositiveId(bggIdProperty.Value, out var bggId)) invalidEntries++;
                        else ids.Add(bggId);
                    }
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray()) Visit(child);
            }
        }
    }

    public static ClubCollectionDocument BuildDocument(IReadOnlyCollection<BggCollectionItem> items,
        out int orphanExpansions)
    {
        var selection = items.Where(item => item.Game.BggId is > 0).Select(item => ToSelection(item)).ToArray();
        var merged = ClubBggImportService.Merge(ClubCollectionDocument.Empty, selection);
        orphanExpansions = merged.OrphanExpansions;
        return merged.Document;
    }

    private static CampImportSelectionItem ToSelection(BggCollectionItem item)
    {
        var game = item.Game;
        var parentId = item.ParentBggIds.FirstOrDefault();
        return new CampImportSelectionItem(game.BggId!.Value,
            item.IsExpansion ? CampContributionItemType.Expansion : CampContributionItemType.BaseGame,
            parentId > 0 ? parentId : null,
            game.Name, true, game.ThumbnailImageUrl, game.ImageUrl, game.MinPlayers, game.MaxPlayers,
            game.BestPlayers, game.Types, game.Categories, game.Description, game.YearPublished,
            game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge, game.Type, game.Subdomains,
            game.CategoryItems, game.Mechanics, item.ParentBggIds);
    }

    private static bool TryReadPositiveId(JsonElement element, out long bggId)
    {
        bggId = 0;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out bggId)) return bggId > 0;
        return element.ValueKind == JsonValueKind.String
            && long.TryParse(element.GetString(), out bggId)
            && bggId > 0;
    }

    private static string? Argument(IEnumerable<string> args, string prefix) => args
        .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}
