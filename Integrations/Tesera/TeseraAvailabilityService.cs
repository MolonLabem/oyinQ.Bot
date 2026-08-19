using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace oyinQ.Bot.Integrations.Tesera;

public sealed record TeseraAvailabilitySnapshot(
    bool IsAvailable,
    string Reason,
    DateTimeOffset CheckedAt);

public sealed class TeseraAvailabilityService(
    ITeseraClient teseraClient,
    IMemoryCache memoryCache,
    ILogger<TeseraAvailabilityService> logger)
{
    private const string CacheKey = "tesera:availability";
    private const string ProbeAlias = "carcassonne";
    private static readonly TimeSpan AvailableCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UnavailableCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim gate = new(1, 1);

    public bool IsKnownUnavailable =>
        memoryCache.TryGetValue<TeseraAvailabilitySnapshot>(CacheKey, out var snapshot)
        && snapshot is { IsAvailable: false };

    public async Task<TeseraAvailabilitySnapshot> GetAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh
            && memoryCache.TryGetValue<TeseraAvailabilitySnapshot>(CacheKey, out var cached)
            && cached is not null)
        {
            return cached;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh
                && memoryCache.TryGetValue<TeseraAvailabilitySnapshot>(CacheKey, out cached)
                && cached is not null)
            {
                return cached;
            }

            var snapshot = await ProbeAsync(cancellationToken);
            memoryCache.Set(
                CacheKey,
                snapshot,
                snapshot.IsAvailable ? AvailableCacheDuration : UnavailableCacheDuration);
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TeseraAvailabilitySnapshot> ProbeAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            var game = await teseraClient.GetGameByAliasAsync(ProbeAlias, timeout.Token);
            if (game is null)
            {
                return Unavailable("unexpected_response");
            }

            return new TeseraAvailabilitySnapshot(true, "ok", DateTimeOffset.UtcNow);
        }
        catch (TeseraUnavailableException exception)
        {
            var reason = Classify(exception.Message);
            logger.LogInformation("Tesera availability probe failed: {Reason}.", reason);
            return Unavailable(reason);
        }
        catch (HttpRequestException exception)
        {
            var reason = exception.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "http_401",
                HttpStatusCode.Forbidden => "http_403",
                _ => "http_error"
            };
            logger.LogInformation(exception, "Tesera availability probe failed: {Reason}.", reason);
            return Unavailable(reason);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Tesera availability probe timed out.");
            return Unavailable("timeout");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tesera availability probe failed unexpectedly.");
            return Unavailable("error");
        }
    }

    private static TeseraAvailabilitySnapshot Unavailable(string reason) =>
        new(false, reason, DateTimeOffset.UtcNow);

    private static string Classify(string message)
    {
        if (message.Contains("403", StringComparison.Ordinal))
        {
            return "http_403";
        }

        if (message.Contains("401", StringComparison.Ordinal))
        {
            return "http_401";
        }

        return "unavailable";
    }
}
