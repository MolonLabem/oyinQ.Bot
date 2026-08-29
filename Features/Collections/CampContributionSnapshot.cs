using System.Text.Json;

namespace oyinQ.Bot.Features.Collections;

public sealed record CampContributionSnapshot(
    int Version,
    string Name,
    string? ThumbnailImageUrl,
    string? ImageUrl,
    int? MinPlayers,
    int? MaxPlayers,
    string? BestPlayers,
    IReadOnlyList<string>? Types = null,
    IReadOnlyList<string>? Categories = null)
{
    public const int CurrentVersion = 1;
}

public static class CampContributionSnapshotSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(CampContributionSnapshot snapshot)
    {
        Validate(snapshot);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static CampContributionSnapshot Deserialize(string json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<CampContributionSnapshot>(json, JsonOptions)
                ?? throw new InvalidOperationException("Снимок вклада игры пуст.");
            Validate(snapshot);
            return snapshot;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Снимок вклада игры повреждён.", exception);
        }
    }

    private static void Validate(CampContributionSnapshot snapshot)
    {
        if (snapshot.Version != CampContributionSnapshot.CurrentVersion)
            throw new InvalidOperationException($"Версия снимка вклада {snapshot.Version} не поддерживается.");
        if (string.IsNullOrWhiteSpace(snapshot.Name) || snapshot.Name.Length > 300)
            throw new InvalidOperationException("Название игры во вкладе некорректно.");
        if (snapshot.ThumbnailImageUrl?.Length > 1000 || snapshot.ImageUrl?.Length > 1000)
            throw new InvalidOperationException("URL изображения во вкладе слишком длинный.");
        if (snapshot.MinPlayers is < 1 || snapshot.MaxPlayers is < 1
            || snapshot.MinPlayers > snapshot.MaxPlayers)
            throw new InvalidOperationException("Количество игроков во вкладе некорректно.");
        if (snapshot.BestPlayers?.Length > 64)
            throw new InvalidOperationException("Описание лучшего количества игроков слишком длинное.");
        if ((snapshot.Types?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 100) ?? false)
            || (snapshot.Categories?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 100) ?? false))
            throw new InvalidOperationException("Теги игры во вкладе некорректны.");
    }
}
