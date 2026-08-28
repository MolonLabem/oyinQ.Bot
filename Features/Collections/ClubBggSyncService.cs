using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public sealed record ClubBggSyncItem(long BggId, string Name);

public sealed record ClubBggOrphanExpansion(
    long BggId,
    string Name,
    IReadOnlyList<long> ParentBggIds);

public sealed record ClubBggSyncPreview(
    string Username,
    ClubCollectionDocument Document,
    IReadOnlyList<ClubBggSyncItem> Added,
    IReadOnlyList<ClubBggSyncItem> Removed,
    IReadOnlyList<ClubBggSyncItem> Changed,
    IReadOnlyList<ClubBggOrphanExpansion> OrphanExpansions)
{
    public bool IsEmpty => Document.Games.Count == 0;
}

public sealed class ClubBggSyncService(IBoardGameGeekClient bggClient)
{
    public async Task<ClubBggSyncPreview> PreviewAsync(
        string username,
        ClubCollectionDocument current,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ClubCollectionSerializer.Validate(current);

        var normalizedUsername = username.Trim();
        var baseGames = await bggClient.GetOwnedBaseGamesAsync(normalizedUsername, cancellationToken);
        var expansions = await bggClient.GetOwnedExpansionsAsync(normalizedUsername, cancellationToken);

        var ownedBaseGames = baseGames
            .Where(value => value.BggId is > 0)
            .DistinctBy(value => value.BggId!.Value)
            .ToDictionary(value => value.BggId!.Value);
        var expansionsByParent = ownedBaseGames.Keys.ToDictionary(
            value => value,
            _ => new List<ClubCollectionExpansion>());
        var orphans = new List<ClubBggOrphanExpansion>();

        foreach (var ownedExpansion in expansions
                     .Where(value => value.Expansion.BggId is > 0)
                     .DistinctBy(value => value.Expansion.BggId!.Value))
        {
            var expansion = ownedExpansion.Expansion;
            var ownedParentIds = ownedExpansion.ParentBggIds
                .Where(ownedBaseGames.ContainsKey)
                .Distinct()
                .Order()
                .ToArray();
            if (ownedParentIds.Length == 0)
            {
                orphans.Add(new ClubBggOrphanExpansion(
                    expansion.BggId!.Value,
                    expansion.Name,
                    ownedExpansion.ParentBggIds.Distinct().Order().ToArray()));
                continue;
            }

            foreach (var parentId in ownedParentIds)
            {
                expansionsByParent[parentId].Add(new ClubCollectionExpansion(
                    expansion.BggId!.Value,
                    expansion.Name));
            }
        }

        var proposed = new ClubCollectionDocument(
            ClubCollectionDocument.CurrentVersion,
            ownedBaseGames
                .Select(pair =>
                {
                    var game = pair.Value;
                    return new ClubCollectionGame(
                        pair.Key,
                        game.Name,
                        game.ThumbnailImageUrl,
                        game.ImageUrl,
                        game.MinPlayers,
                        game.MaxPlayers,
                        game.BestPlayers,
                        expansionsByParent[pair.Key]
                            .DistinctBy(value => value.BggId)
                            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(value => value.BggId)
                            .ToArray());
                })
                .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.BggId)
                .ToArray());
        ClubCollectionSerializer.Validate(proposed);

        var currentById = current.Games.ToDictionary(value => value.BggId);
        var proposedById = proposed.Games.ToDictionary(value => value.BggId);
        var added = proposed.Games
            .Where(value => !currentById.ContainsKey(value.BggId))
            .Select(ToItem)
            .ToArray();
        var removed = current.Games
            .Where(value => !proposedById.ContainsKey(value.BggId))
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.BggId)
            .Select(ToItem)
            .ToArray();
        var changed = proposed.Games
            .Where(value => currentById.TryGetValue(value.BggId, out var existing)
                && !HasSameSnapshot(existing, value))
            .Select(ToItem)
            .ToArray();

        return new ClubBggSyncPreview(
            normalizedUsername,
            proposed,
            added,
            removed,
            changed,
            orphans.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.BggId)
                .ToArray());
    }

    private static ClubBggSyncItem ToItem(ClubCollectionGame value) =>
        new(value.BggId, value.Name);

    private static bool HasSameSnapshot(ClubCollectionGame left, ClubCollectionGame right) =>
        left.BggId == right.BggId
        && left.Name == right.Name
        && left.ThumbnailImageUrl == right.ThumbnailImageUrl
        && left.ImageUrl == right.ImageUrl
        && left.MinPlayers == right.MinPlayers
        && left.MaxPlayers == right.MaxPlayers
        && left.BestPlayers == right.BestPlayers
        && NormalizeExpansions(left.Expansions)
            .SequenceEqual(NormalizeExpansions(right.Expansions));

    private static IEnumerable<(long BggId, string Name)> NormalizeExpansions(
        IReadOnlyList<ClubCollectionExpansion> expansions) =>
        expansions
            .Select(value => (value.BggId, value.Name))
            .OrderBy(value => value.BggId)
            .ThenBy(value => value.Name, StringComparer.Ordinal);
}
