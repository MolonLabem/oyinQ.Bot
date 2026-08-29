using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public sealed class CampBggImportService(IBoardGameGeekClient bggClient)
{
    public async Task<CampBggImportDraft> LoadDraftAsync(string username, CancellationToken cancellationToken,
        Func<int, int, Task>? reportProgress = null)
    {
        var selection = await LoadSelectionAsync(username, cancellationToken, reportProgress);
        return new CampBggImportDraft(
            CampBggImportDraft.CurrentVersion,
            username.Trim(),
            selection.Select(value => new CampBggImportDraftItem(
                value.BggId,
                value.ItemType,
                value.ParentBggId,
                new CampContributionSnapshot(
                    CampContributionSnapshot.CurrentVersion,
                    value.Name,
                    value.ThumbnailImageUrl,
                    value.ImageUrl,
                    value.MinPlayers,
                    value.MaxPlayers,
                    value.BestPlayers))).ToArray());
    }

    public async Task<IReadOnlyList<CampImportSelectionItem>> LoadSelectionAsync(
        string username,
        CancellationToken cancellationToken,
        Func<int, int, Task>? reportProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        // These are deliberately separate BGG collection requests. The default
        // collection response mixes item types and cannot preserve ownership of
        // expansions reliably.
        var baseGames = await bggClient.GetOwnedBaseGamesAsync(username.Trim(), cancellationToken);
        if (reportProgress is not null) await reportProgress(1, 2);
        var expansions = await bggClient.GetOwnedExpansionsAsync(username.Trim(), cancellationToken);
        if (reportProgress is not null) await reportProgress(2, 2);
        var ownedBaseIds = baseGames.Where(value => value.BggId is > 0)
            .Select(value => value.BggId!.Value)
            .ToHashSet();

        var items = new List<CampImportSelectionItem>();
        items.AddRange(baseGames.Where(value => value.BggId is > 0).Select(value =>
            new CampImportSelectionItem(
                value.BggId!.Value,
                CampContributionItemType.BaseGame,
                null,
                value.Name,
                true,
                value.ThumbnailImageUrl,
                value.ImageUrl,
                value.MinPlayers,
                value.MaxPlayers,
                value.BestPlayers)));
        items.AddRange(expansions.Where(value => value.Expansion.BggId is > 0).Select(value =>
        {
            var parentId = value.ParentBggIds.FirstOrDefault(ownedBaseIds.Contains);
            if (parentId <= 0) parentId = value.ParentBggIds.FirstOrDefault();
            return new CampImportSelectionItem(
                value.Expansion.BggId!.Value,
                CampContributionItemType.Expansion,
                parentId > 0 ? parentId : null,
                value.Expansion.Name,
                true,
                value.Expansion.ThumbnailImageUrl,
                value.Expansion.ImageUrl,
                value.Expansion.MinPlayers,
                value.Expansion.MaxPlayers,
                value.Expansion.BestPlayers);
        }));

        return items
            .DistinctBy(value => new { value.BggId, value.ItemType })
            .OrderBy(value => value.ItemType)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<CampImportSelectionGroup> BuildGroups(
        IReadOnlyCollection<CampImportSelectionItem> items)
    {
        var expansions = items.Where(value => value.ItemType == CampContributionItemType.Expansion).ToArray();
        var groups = items.Where(value => value.ItemType == CampContributionItemType.BaseGame)
            .Select(baseGame =>
            {
                var nested = expansions.Where(value => value.ParentBggId == baseGame.BggId).ToArray();
                return new CampImportSelectionGroup(
                    baseGame,
                    nested,
                    nested.Any(value => CampContributionSelectionService.NeedsMissingBaseWarning(value, items)));
            })
            .ToList();

        foreach (var orphan in expansions.Where(value => value.ParentBggId is null
                     || items.All(baseGame => baseGame.ItemType != CampContributionItemType.BaseGame
                         || baseGame.BggId != value.ParentBggId)))
        {
            var syntheticParent = new CampImportSelectionItem(
                orphan.ParentBggId ?? 0,
                CampContributionItemType.BaseGame,
                null,
                "Базовая игра не найдена в коллекции",
                false);
            groups.Add(new CampImportSelectionGroup(syntheticParent, [orphan], orphan.Selected));
        }

        return groups;
    }
}
