using System.Text.Json;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

public sealed record CampBggImportDraft(int Version, string BggUsername, IReadOnlyList<CampBggImportDraftItem> Items)
{
    public const int CurrentVersion = 1;
}

public sealed record CampBggImportDraftItem(
    long BggId,
    CampContributionItemType ItemType,
    long? ParentBggId,
    CampContributionSnapshot Snapshot,
    bool SelectedByDefault = true);

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
        if (draft.Version != CampBggImportDraft.CurrentVersion)
            throw new InvalidOperationException($"Версия черновика {draft.Version} не поддерживается.");
        if (string.IsNullOrWhiteSpace(draft.BggUsername) || draft.BggUsername.Length > 100)
            throw new InvalidOperationException("Имя пользователя BGG в черновике некорректно.");
        if (draft.Items is null || draft.Items.GroupBy(x => new { x.BggId, x.ItemType }).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Черновик содержит повторяющиеся элементы.");
        foreach (var item in draft.Items)
        {
            if (item.BggId <= 0 || item.ParentBggId is <= 0)
                throw new InvalidOperationException("Черновик содержит некорректный BGG ID.");
            _ = CampContributionSnapshotSerializer.Serialize(item.Snapshot);
        }
    }
}
