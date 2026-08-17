using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Games;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Features.Interests;

public sealed class InterestsHandler(
    AppDbContext dbContext,
    GamesHandler gamesHandler)
{
    public async Task<bool> TryHandleCallbackAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var parts = callbackQuery.Data?.Split(':');
        if (parts is not ["interest", "toggle", var gameIdText]
            || !long.TryParse(gameIdText, out var gameId))
        {
            return false;
        }

        var participantId = await dbContext.Participants
            .Where(value => value.TelegramUserId == telegramUserId)
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);

        var gameExists = await dbContext.Games.AnyAsync(
            value => value.Id == gameId,
            cancellationToken);
        if (!gameExists)
        {
            return true;
        }

        var interest = await dbContext.GameInterests.SingleOrDefaultAsync(
            value => value.ParticipantId == participantId && value.GameId == gameId,
            cancellationToken);

        if (interest is null)
        {
            dbContext.GameInterests.Add(new GameInterest
            {
                ParticipantId = participantId,
                GameId = gameId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            dbContext.GameInterests.Remove(interest);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await gamesHandler.ShowGameCardAsync(
            callbackQuery,
            telegramUserId,
            gameId,
            "cp",
            0,
            cancellationToken);
        return true;
    }
}
