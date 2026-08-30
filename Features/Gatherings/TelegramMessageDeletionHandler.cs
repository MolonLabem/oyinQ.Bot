using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot.Exceptions;

namespace oyinQ.Bot.Features.Gatherings;

public enum TelegramMessageDeletionOutcome
{
    Success,
    Retry
}

public sealed record TelegramMessageDeletionResult(
    TelegramMessageDeletionOutcome Outcome,
    string? Error = null);

public sealed class TelegramMessageDeletionHandler(
    ITelegramMessageDeletionClient deletionClient,
    ILogger<TelegramMessageDeletionHandler> logger)
{
    public async Task<TelegramMessageDeletionResult> DeleteAsync(
        long chatId, int messageId, CancellationToken cancellationToken)
    {
        try
        {
            await deletionClient.DeleteAsync(chatId, messageId, cancellationToken);
            return new(TelegramMessageDeletionOutcome.Success);
        }
        catch (ApiRequestException exception) when (IsAlreadyDeleted(exception))
        {
            return new(TelegramMessageDeletionOutcome.Success);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception,
                "Telegram message {TelegramChatId}/{TelegramMessageId} deletion failed and will be retried.",
                chatId, messageId);
            var error = exception.Message.Length <= 2000 ? exception.Message : exception.Message[..2000];
            return new(TelegramMessageDeletionOutcome.Retry, error);
        }
    }

    public static bool IsAlreadyDeleted(ApiRequestException exception) =>
        exception.ErrorCode == 400
        && exception.Message.Contains("message to delete not found", StringComparison.OrdinalIgnoreCase);
}
