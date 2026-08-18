using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Games;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class GameSearchServiceAvailabilityTests
{
    [Fact]
    public async Task SearchExternalAsync_WhenBggDisabled_DoesNotCallClient()
    {
        var client = new TrackingBggClient();
        var service = CreateService(client);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.SearchExternalAsync("Catan", default));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task GetBggGameAsync_WhenBggDisabled_DoesNotCallClient()
    {
        var client = new TrackingBggClient();
        var service = CreateService(client);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetBggGameAsync(13, default));
        Assert.Equal(0, client.CallCount);
    }

    private static GameSearchService CreateService(IBoardGameGeekClient client) =>
        new(
            null!,
            null!,
            client,
            Options.Create(new BggOptions()));

    private sealed class TrackingBggClient : IBoardGameGeekClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ExternalGameSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<ExternalGameSearchResult>>([]);
        }

        public Task<ExternalGame?> GetGameAsync(long bggId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<ExternalGame?>(null);
        }

        public Task<IReadOnlyList<ExternalGame>> GetOwnedCollectionAsync(
            string username,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<ExternalGame>>([]);
        }

        public Task<ExternalCollectionStep> GetOwnedCollectionStepAsync(
            string username,
            int offset,
            int limit,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ExternalCollectionStep([], offset, 0));
        }
    }
}
