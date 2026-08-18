using System.Net;

namespace oyinQ.Bot.Integrations;

public static class HttpRetryHelper
{
    public static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendAsync);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);

        for (var attempt = 1; ; attempt++)
        {
            var response = await sendAsync(cancellationToken);
            if (!ShouldRetry(response.StatusCode) || attempt >= maxAttempts)
            {
                return response;
            }

            response.Dispose();
            if (retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.Accepted
        || statusCode == HttpStatusCode.TooManyRequests
        || statusCode is HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}
