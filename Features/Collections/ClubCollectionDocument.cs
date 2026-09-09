using System.Text.Json;
using System.Text.Json.Serialization;

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
    IReadOnlyList<GameTaxonomyItem>? Mechanics = null,
    string? OriginalName = null)
{
    [JsonIgnore]
    public bool HasExpansions => Expansions is { Count: > 0 };

    public ClubCollectionGame WithMetadataFallback(CollectionItemSnapshot fallback) => this with
    {
        ThumbnailImageUrl = this.ThumbnailImageUrl ?? fallback.ThumbnailImageUrl,
        ImageUrl = this.ImageUrl ?? fallback.ImageUrl,
        Description = this.Description ?? fallback.Description,
        YearPublished = this.YearPublished ?? fallback.YearPublished,
        MinPlayers = this.MinPlayers ?? fallback.MinPlayers,
        MaxPlayers = this.MaxPlayers ?? fallback.MaxPlayers,
        BestPlayers = this.BestPlayers ?? fallback.BestPlayers,
        MinPlayTimeMinutes = this.MinPlayTimeMinutes ?? fallback.MinPlayTimeMinutes,
        MaxPlayTimeMinutes = this.MaxPlayTimeMinutes ?? fallback.MaxPlayTimeMinutes,
        MinAge = this.MinAge ?? fallback.MinAge,
        Type = BggTaxonomyCatalog.ResolveType(this.Type,
            this.Subdomains is { Count: > 0 } ? this.Subdomains : fallback.Subdomains,
            this.Types is { Count: > 0 } ? this.Types : fallback.Types,
            this.CategoryItems is { Count: > 0 } ? this.CategoryItems : fallback.CategoryItems,
            this.Categories is { Count: > 0 } ? this.Categories : fallback.Categories),
        Types = this.Types is { Count: > 0 } ? this.Types : fallback.Types,
        Categories = this.Categories is { Count: > 0 } ? this.Categories : fallback.Categories,
        Subdomains = this.Subdomains is { Count: > 0 } ? this.Subdomains : fallback.Subdomains,
        CategoryItems = this.CategoryItems is { Count: > 0 } ? this.CategoryItems : fallback.CategoryItems,
        Mechanics = this.Mechanics is { Count: > 0 } ? this.Mechanics : fallback.Mechanics,
        OriginalName = string.IsNullOrWhiteSpace(this.OriginalName)
            ? fallback.OriginalName : this.OriginalName
    };
}

public sealed record ClubCollectionExpansion(long BggId, string Name, string? OriginalName = null,
    int? MinPlayers = null, int? MaxPlayers = null)
{
    public static IReadOnlyList<ClubCollectionExpansion> Merge(IEnumerable<ClubCollectionExpansion> existing,
        IEnumerable<ClubCollectionExpansion> additions) => existing.Concat(additions).GroupBy(item => item.BggId)
        .Select(group => group.Aggregate((current, next) => current.WithMetadataFallback(next))).ToArray();

    public ClubCollectionExpansion WithMetadataFallback(ClubCollectionExpansion fallback) => this with
    {
        OriginalName = OriginalName ?? fallback.OriginalName,
        MinPlayers = MinPlayers ?? fallback.MinPlayers,
        MaxPlayers = MaxPlayers ?? fallback.MaxPlayers
    };
}
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
            Type = BggTaxonomyCatalog.ResolveType(game.Type, game.Subdomains, game.Types,
                game.CategoryItems, game.Categories),
            Expansions = game.Expansions ?? [],
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

        if (document.Games.Any(game => game.BggId <= 0 || string.IsNullOrWhiteSpace(game.Name)
            || game.OriginalName?.Length > 300))
        {
            throw new InvalidOperationException("У каждой игры коллекции должны быть название и положительный BGG ID.");
        }

        if (document.Games.GroupBy(game => game.BggId).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Коллекция содержит повторяющиеся игры BGG.");
        }

        foreach (var game in document.Games)
        {
            var expansions = game.Expansions ?? [];
            if (expansions.Any(expansion => expansion.BggId <= 0 || string.IsNullOrWhiteSpace(expansion.Name)
                    || expansion.OriginalName?.Length > 300)
                || expansions.GroupBy(expansion => expansion.BggId).Any(group => group.Count() > 1))
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
