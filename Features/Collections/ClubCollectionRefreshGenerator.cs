using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public sealed record ClubCollectionSourceIds(IReadOnlySet<long> BggIds, int ValidEntries, int InvalidEntries);

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
        var validEntries = 0;
        var invalidEntries = 0;
        foreach (var sourceName in SourceNames)
        {
            var json = await sourceClient.GetStringAsync($"{sourceBaseUrl}{sourceName}.json", cancellationToken);
            var source = ExtractBggIds(json);
            ids.UnionWith(source.BggIds);
            validEntries += source.ValidEntries;
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
        var baseGames = items.Count(item => !item.IsExpansion);
        var expansions = items.Count(item => item.IsExpansion);
        var batches = (ids.Count + 19) / 20;
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(output, ClubCollectionSerializer.Serialize(document) + Environment.NewLine,
            cancellationToken);

        Console.WriteLine($"Read {validEntries} positive BGG ID entries and {ids.Count} unique IDs from club, guests, john, and sergei; "
            + $"skipped {invalidEntries} malformed or missing IDs.");
        Console.WriteLine($"BGG used {batches} /thing batches of at most 20 IDs and resolved {items.Count} "
            + $"canonical items ({baseGames} base games, {expansions} expansions). Excluded {orphanExpansions} expansions "
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
        if (Argument(args, "--apply-club-key=") is { Length: > 0 } clubKey)
            await ApplyToClubAsync(configuration, clubKey, document, cancellationToken);
        else if (args.Any(value => string.Equals(value, "--apply-only-club", StringComparison.OrdinalIgnoreCase)))
            await ApplyToOnlyClubAsync(configuration, document, cancellationToken);
        return 0;
    }

    public static ClubCollectionSourceIds ExtractBggIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        var ids = new HashSet<long>();
        var validEntries = 0;
        var invalidEntries = 0;
        Visit(document.RootElement);
        return new ClubCollectionSourceIds(ids, validEntries, invalidEntries);

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
                        else
                        {
                            validEntries++;
                            ids.Add(bggId);
                        }
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

    private static async Task ApplyToClubAsync(IConfiguration configuration, string clubKey,
        ClubCollectionDocument document, CancellationToken cancellationToken)
    {
        var connectionString = configuration["Database:ConnectionString"]?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database__ConnectionString is required with --apply-club-key.");
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var dbContext = new AppDbContext(dbOptions);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var club = await dbContext.Clubs.FromSqlInterpolated(
                $"SELECT * FROM \"Clubs\" WHERE \"BotChatKey\" = {clubKey} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Club '{clubKey}' was not found.");
        var current = ClubCollectionSerializer.Deserialize(club.CollectionJson);
        var serialized = ClubCollectionSerializer.Serialize(document);
        var oldIds = CollectionIds(current);
        var newIds = CollectionIds(document);
        var oldLinks = ExpansionLinks(current);
        var newLinks = ExpansionLinks(document);
        var changed = !string.Equals(ClubCollectionSerializer.Serialize(current), serialized,
            StringComparison.Ordinal);
        if (changed)
        {
            club.CollectionJson = serialized;
            club.CollectionRevision++;
            club.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        Console.WriteLine($"Applied Club '{clubKey}' reconciliation: changed={changed}, "
            + $"added={newIds.Except(oldIds).Count()}, retained={newIds.Intersect(oldIds).Count()}, "
            + $"removed={oldIds.Except(newIds).Count()}, expansion-links-added={newLinks.Except(oldLinks).Count()}, "
            + $"expansion-links-removed={oldLinks.Except(newLinks).Count()}, revision={club.CollectionRevision}.");
    }

    private static async Task ApplyToOnlyClubAsync(IConfiguration configuration,
        ClubCollectionDocument document, CancellationToken cancellationToken)
    {
        var connectionString = configuration["Database:ConnectionString"]?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database__ConnectionString is required with --apply-only-club.");
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var dbContext = new AppDbContext(dbOptions);
        var clubKeys = await dbContext.Clubs.AsNoTracking().Select(club => club.BotChatKey)
            .Take(2).ToArrayAsync(cancellationToken);
        if (clubKeys.Length != 1)
            throw new InvalidOperationException($"--apply-only-club requires exactly one Club, but found {clubKeys.Length}{(clubKeys.Length == 2 ? " or more" : string.Empty)}.");
        await ApplyToClubAsync(configuration, clubKeys[0], document, cancellationToken);
    }

    private static HashSet<long> CollectionIds(ClubCollectionDocument document) => document.Games
        .Select(game => game.BggId).Concat(document.Games.SelectMany(game => game.Expansions)
            .Select(expansion => expansion.BggId)).ToHashSet();

    private static HashSet<(long BaseId, long ExpansionId)> ExpansionLinks(ClubCollectionDocument document) =>
        document.Games.SelectMany(game => game.Expansions.Select(expansion =>
            (game.BggId, expansion.BggId))).ToHashSet();

    private static string? Argument(IEnumerable<string> args, string prefix) => args
        .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}
