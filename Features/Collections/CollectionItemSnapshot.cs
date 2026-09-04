using System.Text.Json;

namespace oyinQ.Bot.Features.Collections;

public sealed record CollectionItemSnapshot(
    int Version,
    string Name,
    string? ThumbnailImageUrl,
    string? ImageUrl,
    int? MinPlayers,
    int? MaxPlayers,
    string? BestPlayers,
    IReadOnlyList<string>? Types = null,
    IReadOnlyList<string>? Categories = null,
    string? Description = null,
    int? YearPublished = null,
    int? MinPlayTimeMinutes = null,
    int? MaxPlayTimeMinutes = null,
    int? MinAge = null,
    GameType Type = GameType.Other,
    IReadOnlyList<GameTaxonomyItem>? Subdomains = null,
    IReadOnlyList<GameTaxonomyItem>? CategoryItems = null,
    IReadOnlyList<GameTaxonomyItem>? Mechanics = null,
    IReadOnlyList<long>? ParentBggIds = null,
    string? OriginalName = null)
{
    public const int CurrentVersion = 3;
    public ClubCollectionGame ToCollectionGame(long bggId) => new(bggId, Name, ThumbnailImageUrl, ImageUrl,
        MinPlayers, MaxPlayers, BestPlayers, [], Types, Categories, Description, YearPublished,
        MinPlayTimeMinutes, MaxPlayTimeMinutes, MinAge,
        BggTaxonomyCatalog.ResolveType(Type, Subdomains, Types, CategoryItems, Categories),
        Subdomains, CategoryItems, Mechanics, OriginalName);
}

public static class CollectionItemSnapshotSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(CollectionItemSnapshot snapshot)
    {
        Validate(snapshot);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static CollectionItemSnapshot Deserialize(string json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<CollectionItemSnapshot>(json, JsonOptions)
                ?? throw new InvalidOperationException("Снимок игры пуст.");
            Validate(snapshot);
            return snapshot;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Снимок игры повреждён.", exception);
        }
    }

    private static void Validate(CollectionItemSnapshot snapshot)
    {
        if (snapshot.Version is not 1 and not 2 and not CollectionItemSnapshot.CurrentVersion)
            throw new InvalidOperationException($"Версия снимка коллекции {snapshot.Version} не поддерживается.");
        if (string.IsNullOrWhiteSpace(snapshot.Name) || snapshot.Name.Length > 300
            || snapshot.OriginalName?.Length > 300)
            throw new InvalidOperationException("Название игры в коллекции некорректно.");
        if (snapshot.ThumbnailImageUrl?.Length > 1000 || snapshot.ImageUrl?.Length > 1000)
            throw new InvalidOperationException("URL изображения в коллекции слишком длинный.");
        if (snapshot.MinPlayers is < 1 || snapshot.MaxPlayers is < 1
            || snapshot.MinPlayers > snapshot.MaxPlayers)
            throw new InvalidOperationException("Количество игроков в коллекции некорректно.");
        if (snapshot.BestPlayers?.Length > 64)
            throw new InvalidOperationException("Описание лучшего количества игроков слишком длинное.");
        if ((snapshot.Types?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 100) ?? false)
            || (snapshot.Categories?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 100) ?? false))
            throw new InvalidOperationException("Теги игры в коллекции некорректны.");
        if (snapshot.Description?.Length > 20_000 || snapshot.MinPlayTimeMinutes is < 0
            || snapshot.MaxPlayTimeMinutes is < 0 || snapshot.MinPlayTimeMinutes > snapshot.MaxPlayTimeMinutes
            || snapshot.MinAge is < 0 or > 100)
            throw new InvalidOperationException("Метаданные игры в коллекции некорректны.");
        if (snapshot.ParentBggIds?.Any(x => x <= 0) == true)
            throw new InvalidOperationException("Связи дополнения с базовыми играми некорректны.");
    }
}
