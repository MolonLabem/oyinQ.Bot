namespace oyinQ.Bot.Integrations.Telegram;

internal static class TelegramStartupRetry
{
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    public static async Task<bool> RunAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        ILogger logger,
        CancellationToken startupToken)
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(startupToken);
                attemptTimeout.CancelAfter(AttemptTimeout);
                await operation(attemptTimeout.Token);
                if (attempt > 1)
                    logger.LogInformation("Telegram {OperationName} succeeded on attempt {Attempt}.",
                        operationName, attempt);
                return true;
            }
            catch (OperationCanceledException) when (startupToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Telegram {OperationName} attempt {Attempt}/{MaximumAttempts} failed: {ErrorType}: {ErrorMessage}",
                    operationName, attempt, MaximumAttempts, exception.GetType().Name, exception.Message);
                logger.LogDebug(exception, "Telegram {OperationName} failure details.", operationName);
                if (attempt < MaximumAttempts)
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), startupToken);
            }
        }

        logger.LogWarning(
            "Telegram {OperationName} was not completed during startup; application startup will continue.",
            operationName);
        return false;
    }
}
