using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public static class RollMoveReconciliation
{
    public static IReadOnlyList<long> ParseCandidates(string text) => WebUtility.HtmlDecode(text)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(value => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id : throw new ArgumentException($"Некорректный BGG ID: {value}"))
        .Distinct().Order().ToArray();

    public static (long[] Owned, long[] Missing, long[] Review, long[] Unresolved) Classify(
        IEnumerable<long> candidates, IReadOnlySet<long> verified, IReadOnlySet<long> owned, IReadOnlySet<long> review)
    {
        var ids = candidates.Distinct().Order().ToArray();
        return (ids.Where(id => !review.Contains(id) && verified.Contains(id) && owned.Contains(id)).ToArray(),
            ids.Where(id => !review.Contains(id) && verified.Contains(id) && !owned.Contains(id)).ToArray(),
            ids.Where(review.Contains).ToArray(),
            ids.Where(id => !review.Contains(id) && !verified.Contains(id)).ToArray());
    }

    public static async Task<int> RunAsync(IConfiguration configuration, CancellationToken ct = default)
    {
        const string directory = "Data/Imports/RollMove";
        var candidates = ParseCandidates(await File.ReadAllTextAsync($"{directory}/reconciliation-candidates.txt", ct));
        var reviewPath = $"{directory}/edition-review-ids.txt";
        var review = File.Exists(reviewPath) ? ParseCandidates(await File.ReadAllTextAsync(reviewPath, ct)).ToHashSet() : [];
        var options = BggOptions.FromConfiguration(configuration);
        if (!options.IsAvailable) throw new InvalidOperationException("Для сверки нужен серверный токен BGG.");
        using var http = new HttpClient { BaseAddress = new Uri("https://boardgamegeek.com"), Timeout = TimeSpan.FromSeconds(60) };
        var bgg = new BoardGameGeekClient(http, Options.Create(options));
        var items = await bgg.GetItemsByIdsAsync(candidates, ct);
        Console.WriteLine($"BGG подтвердил {items.Count} из {candidates.Count} ID.");
        var ownedItems = await new CampBggImportService(bgg).LoadSelectionAsync("RollMoveClub", ct);
        var result = Classify(candidates, items.Select(x => x.Game.BggId!.Value).ToHashSet(),
            ownedItems.Select(x => x.BggId).ToHashSet(), review);
        var path = $"{directory}/club-collection.v2.json";
        var current = ClubCollectionSerializer.Deserialize(await File.ReadAllTextAsync(path, ct));
        var accepted = items.Where(x => !review.Contains(x.Game.BggId!.Value)).ToArray();
        var merge = ClubBggImportService.Merge(current, accepted.Select(ClubCollectionRefreshGenerator.ToSelection).ToArray());
        await File.WriteAllTextAsync(path, ClubCollectionSerializer.Serialize(merge.Document) + Environment.NewLine, ct);
        var report = new { CheckedAt = DateTimeOffset.UtcNow, Account = "RollMoveClub", AlreadyOwned = result.Owned,
            MissingVerified = result.Missing, EditionReview = result.Review, Unresolved = result.Unresolved,
            merge.AddedGames, merge.AddedExpansions, merge.OrphanExpansions,
            Items = items.Select(x => new { BggId = x.Game.BggId, x.Game.Name, x.Game.OriginalName, x.IsExpansion }),
            AccountMutated = false };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await File.WriteAllTextAsync($"{directory}/reconciliation-report.json", json, ct);
        await File.WriteAllTextAsync($"{directory}/bgg-missing-owned.csv", "objectid,own\n"
            + string.Join('\n', result.Missing.Select(id => $"{id},1")) + "\n", ct);
        Console.WriteLine(json);
        return 0;
    }
}
