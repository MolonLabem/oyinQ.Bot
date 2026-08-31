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

    [Fact]
    public void CurrentSnapshot_IsValidV2AndMatchesCurrentOwnedRelationships()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var path = Path.Combine(repositoryRoot, "Data", "Imports", "RollMove", "club-collection.v2.json");

        var document = ClubCollectionSerializer.Deserialize(File.ReadAllText(path));
        var expansions = document.Games.SelectMany(game => game.Expansions).ToArray();

        Assert.Equal(ClubCollectionDocument.CurrentVersion, document.Version);
        Assert.Equal(219, document.Games.Count);
        Assert.Equal(119, expansions.Length);
        Assert.Equal(109, expansions.Select(expansion => expansion.BggId).Distinct().Count());
        Assert.Contains(document.Games, game => (game.Subdomains?.Count ?? 0) > 1);
        Assert.All(document.Games, game => Assert.NotEmpty(game.CategoryItems ?? []));
        Assert.All(document.Games, game => Assert.NotEmpty(game.Mechanics ?? []));
    }
}
