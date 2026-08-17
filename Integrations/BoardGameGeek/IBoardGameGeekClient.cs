namespace oyinQ.Bot.Integrations.BoardGameGeek;

public sealed record ExternalGame(
    long? BggId,
    string? TeseraAlias,
    string Name,
    int? MinPlayers,
    int? MaxPlayers,
    string? BestPlayers,
    string? ExternalUrl);

public sealed record ExternalGameSearchResult(
    long BggId,
    string Name,
    int? YearPublished);

public interface IBoardGameGeekClient
{
    Task<IReadOnlyList<ExternalGameSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken);

    Task<ExternalGame?> GetGameAsync(
        long bggId,
        CancellationToken cancellationToken);
}
