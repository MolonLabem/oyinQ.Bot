using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Tests;

public sealed class RollMoveImportArtifactTests
{
    [Fact]
    public void InitialSnapshot_IsValidAndMatchesReviewedBggImport()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var importDirectory = Path.Combine(repositoryRoot, "Data", "Imports", "RollMove");
        var document = ClubCollectionSerializer.Deserialize(
            File.ReadAllText(Path.Combine(importDirectory, "club-collection.v1.json")));
        var expansions = document.Games.SelectMany(value => value.Expansions).ToArray();
        var ownedIds = File.ReadAllText(Path.Combine(importDirectory, "bgg-owned-ids.csv"))
            .Trim()
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(219, document.Games.Count);
        Assert.Equal(119, expansions.Length);
        Assert.Equal(109, expansions.Select(value => value.BggId).Distinct().Count());
        Assert.Equal(340, ownedIds.Length);
        Assert.Equal(340, ownedIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(364, File.ReadLines(Path.Combine(importDirectory, "match-audit.csv")).Count());
    }
}
