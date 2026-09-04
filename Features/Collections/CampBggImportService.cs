using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public sealed class CampBggImportService(IBoardGameGeekClient bggClient)
{
    public static CampBggImportDraft ClassifySkips(CampBggImportDraft draft,
        IReadOnlySet<long> baseCollectionIds,
        IReadOnlySet<(long BggId, CollectionItemType ItemType)> currentParticipantManualItems) =>
        draft with { Items = draft.Items.Select(item =>
        {
            if (item.SkipReason is CampImportSkipReason.InvalidOrUnsupportedItem or CampImportSkipReason.ProviderDataIncomplete) return item;
            if (currentParticipantManualItems.Contains((item.BggId, item.ItemType)))
                return item with { SelectedByDefault = false, SkipReason = CampImportSkipReason.AlreadyAddedManually };
            if (baseCollectionIds.Contains(item.BggId))
                return item with { SelectedByDefault = false, SkipReason = CampImportSkipReason.AlreadyInBaseCollection, IsOverridable = true };
            return item;
        }).ToArray() };

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
                new CollectionItemSnapshot(
                    CollectionItemSnapshot.CurrentVersion,
                    value.Name,
                    value.ThumbnailImageUrl,
                    value.ImageUrl,
                    value.MinPlayers,
                    value.MaxPlayers,
                    value.BestPlayers,
                    value.Types,
                    value.Categories,
                    value.Description,
                    value.YearPublished,
                    value.MinPlayTimeMinutes,
                    value.MaxPlayTimeMinutes,
                    value.MinAge,
                    value.Type,
                    value.Subdomains,
                    value.CategoryItems,
                    value.Mechanics,
                    value.ParentBggIds,
                    value.OriginalName),
                ParentBggIds: value.ParentBggIds)).ToArray());
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
                CollectionItemType.BaseGame,
                null,
                value.Name,
                true,
                value.ThumbnailImageUrl,
                value.ImageUrl,
                value.MinPlayers,
                value.MaxPlayers,
                value.BestPlayers,
                value.Types,
                value.Categories,
                value.Description, value.YearPublished, value.MinPlayTimeMinutes, value.MaxPlayTimeMinutes,
                value.MinAge, value.Type, value.Subdomains, value.CategoryItems, value.Mechanics,
                OriginalName: value.OriginalName)));
        items.AddRange(expansions.Where(value => value.Expansion.BggId is > 0).Select(value =>
        {
            var parentIds = value.ParentBggIds.Where(x => x > 0).Distinct().ToArray();
            var parentId = parentIds.FirstOrDefault(ownedBaseIds.Contains);
            if (parentId <= 0) parentId = value.ParentBggIds.FirstOrDefault();
            return new CampImportSelectionItem(
                value.Expansion.BggId!.Value,
                CollectionItemType.Expansion,
                parentId > 0 ? parentId : null,
                value.Expansion.Name,
                true,
                value.Expansion.ThumbnailImageUrl,
                value.Expansion.ImageUrl,
                value.Expansion.MinPlayers,
                value.Expansion.MaxPlayers,
                value.Expansion.BestPlayers,
                value.Expansion.Types,
                value.Expansion.Categories,
                value.Expansion.Description, value.Expansion.YearPublished,
                value.Expansion.MinPlayTimeMinutes, value.Expansion.MaxPlayTimeMinutes,
                value.Expansion.MinAge, value.Expansion.Type, value.Expansion.Subdomains,
                value.Expansion.CategoryItems, value.Expansion.Mechanics, parentIds,
                value.Expansion.OriginalName);
        }));

        return items
            .DistinctBy(value => new { value.BggId, value.ItemType })
            .OrderBy(value => value.ItemType)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

}
