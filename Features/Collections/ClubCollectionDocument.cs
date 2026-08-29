using System.Text.Json;

namespace oyinQ.Bot.Features.Collections;

public sealed record ClubCollectionDocument(int Version, IReadOnlyList<ClubCollectionGame> Games)
{
    public const int CurrentVersion = 1;
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
    IReadOnlyList<string>? Categories = null);

public sealed record ClubCollectionExpansion(long BggId, string Name);

public static class ClubCollectionSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ClubCollectionDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(document, Options);
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
        return document;
    }

    public static void Validate(ClubCollectionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version != ClubCollectionDocument.CurrentVersion)
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
        }
    }
}
