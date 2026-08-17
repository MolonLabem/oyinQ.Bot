using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Integrations.Tesera;

public interface ITeseraClient
{
    Task<IReadOnlyList<ExternalGame>> GetOwnedCollectionAsync(
        string username,
        CancellationToken cancellationToken);

    Task<ExternalGame?> GetGameByAliasAsync(
        string alias,
        CancellationToken cancellationToken);
}

public sealed class TeseraUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
