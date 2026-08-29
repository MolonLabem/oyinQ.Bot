namespace oyinQ.Bot.Integrations;

public sealed record ExternalGame(
    long? BggId,
    string Name,
    int? MinPlayers,
    int? MaxPlayers,
    string? BestPlayers,
    string? ExternalUrl,
    string? ThumbnailImageUrl = null,
    string? ImageUrl = null,
    IReadOnlyList<string>? Types = null,
    IReadOnlyList<string>? Categories = null);

public sealed record ExternalCollectionStep(
    IReadOnlyList<ExternalGame> Games,
    int NextOffset,
    int Total);
