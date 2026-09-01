using oyinQ.Bot.Features.Collections;

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
    IReadOnlyList<string>? Categories = null,
    string? Description = null,
    int? YearPublished = null,
    int? MinPlayTimeMinutes = null,
    int? MaxPlayTimeMinutes = null,
    int? MinAge = null,
    IReadOnlyList<GameTaxonomyItem>? Subdomains = null,
    IReadOnlyList<GameTaxonomyItem>? CategoryItems = null,
    IReadOnlyList<GameTaxonomyItem>? Mechanics = null,
    GameType Type = GameType.Other);
