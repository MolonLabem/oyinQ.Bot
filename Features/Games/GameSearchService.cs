using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Normalization;
using oyinQ.Bot.Data;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Games;

public sealed class GameSearchService(
    AppDbContext dbContext,
    GameNameNormalizer normalizer,
    IBoardGameGeekClient boardGameGeekClient)
{
    public Task<IReadOnlyList<ExternalGameSearchResult>> SearchExternalAsync(
        string query,
        CancellationToken cancellationToken) =>
        boardGameGeekClient.SearchAsync(query, cancellationToken);

    public Task<ExternalGame?> GetBggGameAsync(
        long bggId,
        CancellationToken cancellationToken) =>
        boardGameGeekClient.GetGameAsync(bggId, cancellationToken);

    public async Task<IReadOnlyList<long>> SearchCatalogIdsAsync(
        string query,
        long? telegramUserId,
        CancellationToken cancellationToken)
    {
        var normalized = normalizer.Normalize(query);
        if (normalized.Length < 2)
        {
            return [];
        }

        var games = dbContext.Games
            .AsNoTracking()
            .Where(game => game.NormalizedName.Contains(normalized));

        if (telegramUserId is { } userId)
        {
            games = games.Where(game => game.Copies.Any(copy =>
                copy.Source == Data.Entities.GameCopySource.Personal
                && copy.OwnerParticipant != null
                && copy.OwnerParticipant.TelegramUserId == userId));
        }
        else
        {
            games = games.Where(game => game.Copies.Any());
        }

        return await games
            .OrderByDescending(game => game.Interests.Count)
            .ThenBy(game => game.Name)
            .Select(game => game.Id)
            .Take(10)
            .ToArrayAsync(cancellationToken);
    }
}
