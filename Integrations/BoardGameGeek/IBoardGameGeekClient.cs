using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public sealed record ExternalGameSearchResult(
    long BggId,
    string Name,
    int? YearPublished);

public sealed record BggExpansion(long BggId, string Name);

public sealed record BggGameDetails(
    ExternalGame Game,
    IReadOnlyList<BggExpansion> Expansions);

public sealed record BggOwnedExpansion(
    ExternalGame Expansion,
    IReadOnlyList<long> ParentBggIds);

public sealed record BggCollectionItem(
    ExternalGame Game,
    bool IsExpansion,
    IReadOnlyList<long> ParentBggIds);

public interface IBoardGameGeekClient
{
    Task<IReadOnlyList<ExternalGameSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken);

    Task<BggGameDetails?> GetGameDetailsAsync(
        long bggId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(
        string username,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(
        string username,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(
        IReadOnlyCollection<long> bggIds,
        CancellationToken cancellationToken);
}
