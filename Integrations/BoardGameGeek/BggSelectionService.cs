using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public enum BggSelectionPurpose { BaseGame, Ownership, Wish }

public sealed record BggSelectionPreview(BggGameDetails Details, CollectionItemType ItemType,
    IReadOnlyList<long> SelectedExpansionIds, IReadOnlyList<BggBaseGameSearchResult> BaseGames);

// One selection boundary for every adapter. Search labels are never item-type authority.
public sealed class BggSelectionService(IBoardGameGeekClient client)
{
    public async Task<BggGameDetails> LoadBaseSelectionAsync(long id, IReadOnlyCollection<long> selected, CancellationToken ct)
    {
        var details = await RequireBaseGameAsync(id, ct);
        var missing = selected.Except(details.Expansions.Select(item => item.BggId)).ToArray();
        if (missing.Length == 0) return details;
        var expansions = (await client.GetItemsByIdsAsync(missing, ct))
            .Where(item => item.IsExpansion && missing.Contains(item.Game.BggId ?? 0) && item.ParentBggIds.Contains(id))
            .Select(BggGameMapper.ToBggExpansion).ToArray();
        if (missing.Except(expansions.Select(item => item.BggId)).Any())
            throw new InvalidOperationException("Дополнение не относится к выбранной базовой игре.");
        return details with { Expansions = details.Expansions.Concat(expansions).ToArray() };
    }

    public async Task<BggGameDetails> RequireBaseGameAsync(long id, CancellationToken ct)
    {
        var details = await client.GetGameDetailsAsync(id, ct)
            ?? throw new KeyNotFoundException("Базовая игра не найдена в BGG.");
        if (details.Game.BggId != id) throw new InvalidOperationException("BGG вернул данные другой игры.");
        return details;
    }

    public async Task<BggSelectionPreview?> PreviewAsync(long id, BggSelectionPurpose purpose,
        long? baseGameId, CancellationToken ct)
    {
        if (baseGameId is null)
        {
            var details = await client.GetGameDetailsAsync(id, ct);
            if (details is not null)
            {
                if (details.Game.BggId != id) throw new InvalidOperationException("BGG вернул данные другой игры.");
                return new(details, CollectionItemType.BaseGame, [], []);
            }
        }
        var expansion = (await client.GetItemsByIdsAsync([id], ct))
            .SingleOrDefault(item => item.Game.BggId == id && item.IsExpansion);
        if (expansion is null) return null;
        if (purpose == BggSelectionPurpose.Wish)
            throw new InvalidOperationException("В вишлист можно добавить только базовую игру. Выберите её в поиске.");
        if (purpose == BggSelectionPurpose.Ownership)
            return new(new(expansion.Game, []), CollectionItemType.Expansion, [], []);
        var parentIds = expansion.ParentBggIds.Where(value => value > 0).Distinct().ToArray();
        if (baseGameId is { } chosenParent)
        {
            if (!parentIds.Contains(chosenParent)) throw new InvalidOperationException("Дополнение не относится к выбранной базовой игре.");
            return await WithBaseAsync(expansion, chosenParent, ct);
        }
        if (parentIds.Length == 1) return await WithBaseAsync(expansion, parentIds[0], ct);
        var parents = (await client.GetItemsByIdsAsync(parentIds, ct))
            .Where(item => !item.IsExpansion && parentIds.Contains(item.Game.BggId ?? 0))
            .Select(item => new BggBaseGameSearchResult(item.Game.BggId!.Value, item.Game.Name,
                item.Game.YearPublished, item.Game.OriginalName)).ToArray();
        if (parents.Length == 0) throw new InvalidOperationException("Для этого дополнения BGG не указал доступную базовую игру.");
        return new(new(expansion.Game, []), CollectionItemType.Expansion, [id], parents);
    }

    private async Task<BggSelectionPreview> WithBaseAsync(BggCollectionItem expansion, long parentId, CancellationToken ct)
    {
        var details = await RequireBaseGameAsync(parentId, ct);
        // The expansion's official inbound parent link is sufficient even when the base response omits it.
        details = details with { Expansions = new[] { BggGameMapper.ToBggExpansion(expansion) }
            .Concat(details.Expansions).DistinctBy(item => item.BggId).ToArray() };
        return new(details, CollectionItemType.BaseGame, [expansion.Game.BggId!.Value], []);
    }

    public async Task<IReadOnlyList<CampBggImportDraftItem>> LoadOwnershipAsync(long id,
        IReadOnlyCollection<long> selectedExpansionIds, CancellationToken ct)
    {
        var ids = selectedExpansionIds.Append(id).ToHashSet();
        var items = (await client.GetItemsByIdsAsync(ids, ct)).Where(item => ids.Contains(item.Game.BggId ?? 0)).ToArray();
        var primary = items.SingleOrDefault(item => item.Game.BggId == id);
        if (primary is null || selectedExpansionIds.Any(expansionId => !items.Any(item =>
                item.Game.BggId == expansionId && item.IsExpansion && item.ParentBggIds.Contains(id))))
            throw new InvalidOperationException("BGG не подтвердил игру или связь дополнения.");
        return items.Select(BggGameMapper.ToDraftItem).ToArray();
    }
}
