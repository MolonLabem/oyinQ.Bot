namespace oyinQ.Bot.Integrations;

public sealed record ExternalGame(
    long? BggId,
    string? TeseraAlias,
    string Name,
    int? MinPlayers,
    int? MaxPlayers,
    string? BestPlayers,
    string? ExternalUrl);

public sealed record ExternalCollectionStep(
    IReadOnlyList<ExternalGame> Games,
    int NextOffset,
    int Total);
