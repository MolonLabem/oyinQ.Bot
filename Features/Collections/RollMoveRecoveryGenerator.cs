using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public static class RollMoveRecoveryGenerator
{
    public static async Task<int> RunAsync(IConfiguration configuration, string[] args,
        CancellationToken cancellationToken = default)
    {
        var options = BggOptions.FromConfiguration(configuration);
        if (!options.IsAvailable)
            throw new InvalidOperationException("BoardGameGeek__ApiToken is required to generate the recovery snapshot.");
        var username = Argument(args, "--username=") ?? "RollMoveClub";
        var output = Argument(args, "--output=") ?? Path.Combine("Data", "Imports", "RollMove",
            "club-collection.v2.json");
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://boardgamegeek.com"),
            Timeout = TimeSpan.FromSeconds(60) };
        var client = new BoardGameGeekClient(httpClient, Options.Create(options));
        var selection = await new CampBggImportService(client).LoadSelectionAsync(username, cancellationToken);
        var merged = ClubBggImportService.Merge(ClubCollectionDocument.Empty, selection);
        var document = merged.Document;
        if (merged.OrphanExpansions > 0)
            Console.WriteLine($"Excluded orphan expansions without an owned base game: {merged.OrphanExpansions}.");
        await File.WriteAllTextAsync(output, ClubCollectionSerializer.Serialize(document) + Environment.NewLine,
            cancellationToken);
        Console.WriteLine($"Wrote {document.Games.Count} base games and "
            + $"{document.Games.SelectMany(game => game.Expansions).Count()} expansion links from {username}.");
        return 0;
    }

    public static void EnsureMembershipUnchanged(ClubCollectionDocument original,
        ClubCollectionDocument enriched)
    {
        var before = original.Games.Select(x => (x.BggId,
            Expansions: string.Join(',', x.Expansions.Select(e => e.BggId).Order()))).OrderBy(x => x.BggId);
        var after = enriched.Games.Select(x => (x.BggId,
            Expansions: string.Join(',', x.Expansions.Select(e => e.BggId).Order()))).OrderBy(x => x.BggId);
        if (!before.SequenceEqual(after))
            throw new InvalidOperationException("Recovery enrichment changed reviewed Club membership.");
    }

    private static string? Argument(IEnumerable<string> args, string prefix) => args
        .FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}
