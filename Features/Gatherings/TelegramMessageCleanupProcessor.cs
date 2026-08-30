using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class TelegramMessageCleanupProcessor(
    AppDbContext dbContext,
    TelegramMessageDeletionHandler deletionHandler,
    TimeProvider timeProvider,
    ILogger<TelegramMessageCleanupProcessor> logger)
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var retryBefore = now - RetryDelay;
        long cleanupId;
        long chatId;
        int messageId;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var cleanup = await dbContext.TelegramMessageCleanups
                .FromSqlInterpolated($$"""
                    SELECT * FROM "TelegramMessageCleanups"
                    WHERE "LastAttemptAt" IS NULL OR "LastAttemptAt" <= {{retryBefore}}
                    ORDER BY "CreatedAt", "Id"
                    FOR UPDATE SKIP LOCKED LIMIT 1
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (cleanup is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            cleanup.AttemptCount++;
            cleanup.LastAttemptAt = now;
            cleanup.LastError = null;
            cleanupId = cleanup.Id;
            chatId = cleanup.TelegramChatId;
            messageId = cleanup.TelegramMessageId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var outcome = await deletionHandler.DeleteAsync(chatId, messageId, cancellationToken);
        if (outcome.Outcome == TelegramMessageDeletionOutcome.Success)
        {
            await dbContext.TelegramMessageCleanups.Where(x => x.Id == cleanupId)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await dbContext.TelegramMessageCleanups.Where(x => x.Id == cleanupId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastError,
                    outcome.Error), CancellationToken.None);
            logger.LogWarning("Telegram message cleanup {CleanupId} will be retried.", cleanupId);
        }

        return true;
    }
}
