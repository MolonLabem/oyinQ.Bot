using System.Text.Json;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

public sealed record CampBggImportDraft(int Version, string BggUsername, IReadOnlyList<CampBggImportDraftItem> Items)
{
    public const int CurrentVersion = 3;
}

public sealed record CampBggImportDraftItem(
    long BggId,
    CollectionItemType ItemType,
    long? ParentBggId,
    CollectionItemSnapshot Snapshot,
    bool SelectedByDefault = true,
    CampImportSkipReason? SkipReason = null,
    bool IsOverridable = false,
    IReadOnlyList<long>? ParentBggIds = null);

public static class CampBggImportDraftSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(CampBggImportDraft draft)
    {
        Validate(draft);
        return JsonSerializer.Serialize(draft, Options);
    }

    public static CampBggImportDraft Deserialize(string? json)
    {
        try
        {
            var draft = JsonSerializer.Deserialize<CampBggImportDraft>(json ?? string.Empty, Options)
                ?? throw new InvalidOperationException("Черновик импорта пуст.");
            Validate(draft);
            return draft;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Черновик импорта повреждён.", exception);
        }
    }

    private static void Validate(CampBggImportDraft draft)
    {
        if (draft.Version is not 1 and not 2 and not CampBggImportDraft.CurrentVersion)
            throw new InvalidOperationException($"Версия черновика {draft.Version} не поддерживается.");
        if (string.IsNullOrWhiteSpace(draft.BggUsername) || draft.BggUsername.Length > 100)
            throw new InvalidOperationException("Имя пользователя BGG в черновике некорректно.");
        if (draft.Items is null || draft.Items.GroupBy(x => new { x.BggId, x.ItemType }).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Черновик содержит повторяющиеся элементы.");
        foreach (var item in draft.Items)
        {
            if (item.BggId <= 0 || item.ParentBggId is <= 0 || item.ParentBggIds?.Any(x => x <= 0) == true)
                throw new InvalidOperationException("Черновик содержит некорректный BGG ID.");
            _ = CollectionItemSnapshotSerializer.Serialize(item.Snapshot);
        }
    }
}
