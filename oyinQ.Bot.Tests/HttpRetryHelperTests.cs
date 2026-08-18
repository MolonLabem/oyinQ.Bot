using System.Net;
using oyinQ.Bot.Integrations;

namespace oyinQ.Bot.Tests;

public sealed class HttpRetryHelperTests
{
    [Fact]
    public async Task SendAsync_RetriesHttp202_ThenReturnsSuccess()
    {
        var calls = 0;

        using var response = await HttpRetryHelper.SendAsync(
            _ => Task.FromResult(new HttpResponseMessage(++calls < 3
                ? HttpStatusCode.Accepted
                : HttpStatusCode.OK)),
            maxAttempts: 3,
            retryDelay: TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task SendAsync_StopsAfterThreeServerErrors()
    {
        var calls = 0;

        using var response = await HttpRetryHelper.SendAsync(
            _ =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            },
            maxAttempts: 3,
            retryDelay: TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task SendAsync_UsesSeparateAcceptedAndTransientAttemptBudgets()
    {
        var calls = 0;

        using var response = await HttpRetryHelper.SendAsync(
            _ =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(calls switch
                {
                    1 or 2 or 3 or 4 => HttpStatusCode.Accepted,
                    _ => HttpStatusCode.OK
                }));
            },
            maxTransientAttempts: 3,
            maxAcceptedAttempts: 5,
            acceptedRetryDelay: TimeSpan.Zero,
            transientRetryDelay: TimeSpan.Zero,
            maxJitter: TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, calls);
    }
}
