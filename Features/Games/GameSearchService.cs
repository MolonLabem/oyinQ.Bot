using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Normalization;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Games;

public sealed class GameSearchService(
    AppDbContext dbContext,
    GameNameNormalizer normalizer,
    IBoardGameGeekClient boardGameGeekClient,
    IOptions<BggOptions> bggOptions)
{
    public bool IsBggAvailable => bggOptions.Value.IsAvailable;

    public Task<IReadOnlyList<ExternalGameSearchResult>> SearchExternalAsync(
        string query,
        CancellationToken cancellationToken)
    {
        EnsureBggAvailable();
        return boardGameGeekClient.SearchAsync(query, cancellationToken);
    }

    public Task<ExternalGame?> GetBggGameAsync(
        long bggId,
        CancellationToken cancellationToken)
    {
        EnsureBggAvailable();
        return boardGameGeekClient.GetGameAsync(bggId, cancellationToken);
    }

    public Task<BggGameDetails?> GetBggGameDetailsAsync(
        long bggId,
        CancellationToken cancellationToken)
    {
        EnsureBggAvailable();
        return boardGameGeekClient.GetGameDetailsAsync(bggId, cancellationToken);
    }

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
                copy.Source == GameCopySource.Personal
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

    private void EnsureBggAvailable()
    {
        if (!bggOptions.Value.IsAvailable)
        {
            throw new HttpRequestException(
                "BGG пока недоступен — ждём подтверждение API-доступа.");
        }
    }
}
