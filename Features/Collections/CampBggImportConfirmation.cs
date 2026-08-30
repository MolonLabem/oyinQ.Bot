using System.Text.Json;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

public sealed record CampImportItemKey(long BggId, CampContributionItemType ItemType);

public sealed record CampBggImportConfirmation(
    int Version,
    IReadOnlyList<long> SelectedBaseGameIds,
    IReadOnlyList<long> SelectedExpansionIds,
    IReadOnlyList<CampImportItemKey> SelectedOverridableItems,
    int Added,
    IReadOnlyDictionary<CampImportSkipReason, int> Skipped)
{
    public const int CurrentVersion = 1;
}

public static class CampBggImportConfirmationSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(CampBggImportConfirmation confirmation)
    {
        Validate(confirmation);
        return JsonSerializer.Serialize(confirmation, Options);
    }

    public static CampBggImportConfirmation Deserialize(string? json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<CampBggImportConfirmation>(json ?? string.Empty, Options)
                ?? throw new InvalidOperationException("Результат подтверждения импорта пуст.");
            Validate(value);
            return value;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Результат подтверждения импорта повреждён.", exception);
        }
    }

    private static void Validate(CampBggImportConfirmation value)
    {
        if (value.Version != CampBggImportConfirmation.CurrentVersion)
            throw new InvalidOperationException($"Версия подтверждения импорта {value.Version} не поддерживается.");
        if (value.Added < 0 || value.SelectedBaseGameIds.Any(x => x <= 0)
            || value.SelectedExpansionIds.Any(x => x <= 0)
            || value.SelectedOverridableItems.Any(x => x.BggId <= 0))
            throw new InvalidOperationException("Результат подтверждения импорта некорректен.");
    }
}
