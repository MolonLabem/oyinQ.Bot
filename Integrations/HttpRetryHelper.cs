using System.Net;

namespace oyinQ.Bot.Integrations;

public static class HttpRetryHelper
{
    public static Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken cancellationToken) =>
        SendAsync(
            sendAsync,
            maxTransientAttempts: maxAttempts,
            maxAcceptedAttempts: maxAttempts,
            acceptedRetryDelay: retryDelay,
            transientRetryDelay: retryDelay,
            maxJitter: TimeSpan.Zero,
            cancellationToken);

    public static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        int maxTransientAttempts,
        int maxAcceptedAttempts,
        TimeSpan acceptedRetryDelay,
        TimeSpan transientRetryDelay,
        TimeSpan maxJitter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendAsync);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTransientAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAcceptedAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(acceptedRetryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(transientRetryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxJitter, TimeSpan.Zero);

        var acceptedAttempts = 0;
        var transientAttempts = 0;

        while (true)
        {
            var response = await sendAsync(cancellationToken);
            TimeSpan? delay = null;

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                acceptedAttempts++;
                if (acceptedAttempts < maxAcceptedAttempts)
                {
                    delay = acceptedRetryDelay;
                }
            }
            else if (IsTransient(response.StatusCode))
            {
                transientAttempts++;
                if (transientAttempts < maxTransientAttempts)
                {
                    var jitter = maxJitter > TimeSpan.Zero
                        ? TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * maxJitter.TotalMilliseconds)
                        : TimeSpan.Zero;
                    delay = transientRetryDelay + jitter;
                }
            }

            if (delay is null)
            {
                return response;
            }

            response.Dispose();
            if (delay.Value > TimeSpan.Zero)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || statusCode is HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}
