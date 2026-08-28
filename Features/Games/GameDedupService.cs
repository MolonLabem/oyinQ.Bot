using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Normalization;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Features.Games;

public sealed class GameDedupService(
    AppDbContext dbContext,
    GameNameNormalizer normalizer)
{
    public async Task<Game> FindOrCreateAsync(
        ExternalGame externalGame,
        CancellationToken cancellationToken)
    {
        var normalizedName = normalizer.Normalize(externalGame.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new InvalidOperationException("Game name cannot be normalized to an empty value.");
        }

        Game? game = null;

        if (externalGame.BggId is { } bggId)
        {
            game = await dbContext.Games.SingleOrDefaultAsync(
                value => value.BggId == bggId,
                cancellationToken);
        }

        game ??= await dbContext.Games
            .OrderBy(value => value.Id)
            .FirstOrDefaultAsync(
                value => value.NormalizedName == normalizedName,
                cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (game is null)
        {
            game = new Game
            {
                BggId = externalGame.BggId,
                Name = externalGame.Name.Trim(),
                NormalizedName = normalizedName,
                MinPlayers = externalGame.MinPlayers,
                MaxPlayers = externalGame.MaxPlayers,
                BestPlayers = externalGame.BestPlayers,
                ExternalUrl = externalGame.ExternalUrl,
                ThumbnailImageUrl = externalGame.ThumbnailImageUrl,
                ImageUrl = externalGame.ImageUrl,
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.Games.Add(game);
        }
        else
        {
            game.BggId ??= externalGame.BggId;
            game.MinPlayers ??= externalGame.MinPlayers;
            game.MaxPlayers ??= externalGame.MaxPlayers;
            game.BestPlayers ??= externalGame.BestPlayers;
            game.ExternalUrl ??= externalGame.ExternalUrl;
            game.ThumbnailImageUrl ??= externalGame.ThumbnailImageUrl;
            game.ImageUrl ??= externalGame.ImageUrl;
            game.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return game;
    }

    public async Task<GameCopy> AddOrUpdatePersonalCopyAsync(
        long gameId,
        long telegramUserId,
        BringStatus bringStatus,
        CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);

        var copy = await dbContext.GameCopies.SingleOrDefaultAsync(
            value => value.GameId == gameId
                && value.OwnerParticipantId == participant.Id
                && value.Source == GameCopySource.Personal,
            cancellationToken);

        if (copy is not null)
        {
            copy.BringStatus = bringStatus;
            await dbContext.SaveChangesAsync(cancellationToken);
            return copy;
        }

        copy = new GameCopy
        {
            GameId = gameId,
            OwnerParticipantId = participant.Id,
            Source = GameCopySource.Personal,
            BringStatus = bringStatus,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.GameCopies.Add(copy);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return copy;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(copy).State = EntityState.Detached;
            var existing = await dbContext.GameCopies.SingleOrDefaultAsync(
                value => value.GameId == gameId
                    && value.OwnerParticipantId == participant.Id
                    && value.Source == GameCopySource.Personal,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            existing.BringStatus = bringStatus;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }
    }

    public async Task<bool> AddImportedCopyIfMissingAsync(
        long gameId,
        long? ownerParticipantId,
        GameCopySource source,
        BringStatus bringStatus,
        CancellationToken cancellationToken)
    {
        if (source == GameCopySource.Personal && ownerParticipantId is null)
        {
            throw new ArgumentException("Personal copies require an owner.", nameof(ownerParticipantId));
        }

        if (source == GameCopySource.Club && ownerParticipantId is not null)
        {
            throw new ArgumentException("Club copies cannot have an owner.", nameof(ownerParticipantId));
        }

        var exists = await dbContext.GameCopies.AnyAsync(
            value => value.GameId == gameId
                && value.OwnerParticipantId == ownerParticipantId
                && value.Source == source,
            cancellationToken);
        if (exists)
        {
            return false;
        }

        var copy = new GameCopy
        {
            GameId = gameId,
            OwnerParticipantId = ownerParticipantId,
            Source = source,
            BringStatus = bringStatus,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.GameCopies.Add(copy);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(copy).State = EntityState.Detached;
            if (await dbContext.GameCopies.AnyAsync(
                    value => value.GameId == gameId
                        && value.OwnerParticipantId == ownerParticipantId
                        && value.Source == source,
                    cancellationToken))
            {
                return false;
            }

            throw;
        }
    }
}
