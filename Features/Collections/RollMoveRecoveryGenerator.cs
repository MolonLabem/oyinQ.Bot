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
        var input = Argument(args, "--input=") ?? Path.Combine("Data", "Imports", "RollMove",
            "club-collection.v1.json");
        var output = Argument(args, "--output=") ?? Path.Combine("Data", "Imports", "RollMove",
            "club-collection.v2.json");
        var original = ClubCollectionSerializer.Deserialize(await File.ReadAllTextAsync(input, cancellationToken));
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://boardgamegeek.com"),
            Timeout = TimeSpan.FromSeconds(60) };
        var client = new BoardGameGeekClient(httpClient, Options.Create(options));
        var enriched = new List<ClubCollectionGame>(original.Games.Count);
        for (var index = 0; index < original.Games.Count; index++)
        {
            var current = original.Games[index];
            var details = await client.GetGameDetailsAsync(current.BggId, cancellationToken)
                ?? throw new InvalidOperationException($"BGG did not return reviewed game {current.BggId}.");
            enriched.Add(ClubMetadataRefreshService.EnrichPreservingMembership(current, details));
            if (index + 1 < original.Games.Count)
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        var document = new ClubCollectionDocument(ClubCollectionDocument.CurrentVersion, enriched);
        EnsureMembershipUnchanged(original, document);
        await File.WriteAllTextAsync(output, ClubCollectionSerializer.Serialize(document) + Environment.NewLine,
            cancellationToken);
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
