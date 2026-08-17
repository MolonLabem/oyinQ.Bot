using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

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

    Task<IReadOnlyList<ExternalGame>> GetOwnedCollectionAsync(
        string username,
        CancellationToken cancellationToken);

    Task<ExternalCollectionStep> GetOwnedCollectionStepAsync(
        string username,
        int offset,
        int limit,
        CancellationToken cancellationToken);
}
