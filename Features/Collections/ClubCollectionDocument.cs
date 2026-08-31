using System.Text.Json;

namespace oyinQ.Bot.Features.Collections;

public sealed record ClubCollectionDocument(int Version, IReadOnlyList<ClubCollectionGame> Games)
{
    public const int CurrentVersion = 2;
    public static ClubCollectionDocument Empty { get; } = new(CurrentVersion, []);
}

public sealed record ClubCollectionGame(
    long BggId,
    string Name,
    string? ThumbnailImageUrl,
    string? ImageUrl,
    int? MinPlayers,
    int? MaxPlayers,
    string? BestPlayers,
    IReadOnlyList<ClubCollectionExpansion> Expansions,
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
    IReadOnlyList<GameTaxonomyItem>? Mechanics = null);

public sealed record ClubCollectionExpansion(long BggId, string Name);
public sealed record GameTaxonomyItem(long BggId, string Name);

public enum GameType
{
    Strategy = 0,
    Family = 1,
    Party = 2,
    Thematic = 3,
    Abstract = 4,
    War = 5,
    Children = 6,
    Customizable = 7,
    Other = 8
}

public static class ClubCollectionSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ClubCollectionDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(Upgrade(document), Options);
    }

    public static ClubCollectionDocument Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Документ коллекции клуба пуст.");
        }

        ClubCollectionDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ClubCollectionDocument>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Документ коллекции клуба содержит некорректный JSON.", exception);
        }

        if (document is null)
        {
            throw new InvalidOperationException("Документ коллекции клуба пуст.");
        }

        Validate(document);
        return Upgrade(document);
    }

    private static ClubCollectionDocument Upgrade(ClubCollectionDocument document) => document with
    {
        Version = ClubCollectionDocument.CurrentVersion,
        Games = document.Games.Select(game => game with
        {
            Type = game.Subdomains?.Count > 0
                ? BggTaxonomyCatalog.MapGameType(game.Subdomains)
                : game.Type != GameType.Other ? game.Type : BggTaxonomyCatalog.InferLegacyType(game.Types),
            Subdomains = game.Subdomains ?? [],
            CategoryItems = game.CategoryItems ?? [],
            Mechanics = game.Mechanics ?? []
        }).ToArray()
    };

    public static void Validate(ClubCollectionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version is not 1 and not ClubCollectionDocument.CurrentVersion)
        {
            throw new InvalidOperationException($"Unsupported club collection version {document.Version}.");
        }

        if (document.Games is null)
        {
            throw new InvalidOperationException("В документе коллекции отсутствует список игр.");
        }

        if (document.Games.Any(game => game.BggId <= 0 || string.IsNullOrWhiteSpace(game.Name)))
        {
            throw new InvalidOperationException("У каждой игры коллекции должны быть название и положительный BGG ID.");
        }

        if (document.Games.GroupBy(game => game.BggId).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Коллекция содержит повторяющиеся игры BGG.");
        }

        foreach (var game in document.Games)
        {
            if (game.Expansions is null
                || game.Expansions.Any(expansion => expansion.BggId <= 0 || string.IsNullOrWhiteSpace(expansion.Name))
                || game.Expansions.GroupBy(expansion => expansion.BggId).Any(group => group.Count() > 1))
            {
                throw new InvalidOperationException($"Club collection game '{game.Name}' has invalid expansions.");
            }
            if ((game.Types?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 100) ?? false)
                || (game.Categories?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 100) ?? false))
                throw new InvalidOperationException($"Club collection game '{game.Name}' has invalid tags.");
            if (game.Description?.Length > 20_000
                || game.YearPublished is < 1000 or > 3000
                || game.MinPlayTimeMinutes is < 0
                || game.MaxPlayTimeMinutes is < 0
                || game.MinPlayTimeMinutes > game.MaxPlayTimeMinutes
                || game.MinAge is < 0 or > 100)
                throw new InvalidOperationException($"Club collection game '{game.Name}' has invalid metadata.");
            ValidateTaxonomy(game.Subdomains, game.Name);
            ValidateTaxonomy(game.CategoryItems, game.Name);
            ValidateTaxonomy(game.Mechanics, game.Name);
        }
    }

    private static void ValidateTaxonomy(IReadOnlyList<GameTaxonomyItem>? items, string gameName)
    {
        if (items is null) return;
        if (items.Any(item => item.BggId <= 0 || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 160)
            || items.GroupBy(item => item.BggId).Any(group => group.Count() > 1))
            throw new InvalidOperationException($"Club collection game '{gameName}' has invalid taxonomy.");
    }
}
