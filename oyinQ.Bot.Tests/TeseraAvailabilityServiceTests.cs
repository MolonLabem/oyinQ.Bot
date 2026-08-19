using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.Tesera;

namespace oyinQ.Bot.Tests;

public sealed class TeseraAvailabilityServiceTests
{
    [Fact]
    public async Task GetAsync_WhenTeseraResponds_CachesAvailableResult()
    {
        var client = new StubTeseraClient
        {
            GameResult = new ExternalGame(
                null,
                "carcassonne",
                "Каркассон",
                2,
                5,
                "3–4",
                "https://tesera.ru/game/carcassonne")
        };
        var service = CreateService(client);

        var first = await service.GetAsync(false, CancellationToken.None);
        var second = await service.GetAsync(false, CancellationToken.None);

        Assert.True(first.IsAvailable);
        Assert.Equal("ok", first.Reason);
        Assert.True(second.IsAvailable);
        Assert.Equal(1, client.GameCalls);
        Assert.False(service.IsKnownUnavailable);
    }

    [Fact]
    public async Task GetAsync_WhenTeseraReturns403_ReportsUnavailableWithoutLeakingMessage()
    {
        var client = new StubTeseraClient
        {
            GameException = new TeseraUnavailableException(
                "Tesera отклонила запрос с сервера бота (403). Internal provider wording.")
        };
        var service = CreateService(client);

        var result = await service.GetAsync(false, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal("http_403", result.Reason);
        Assert.True(service.IsKnownUnavailable);
        Assert.Equal(1, client.GameCalls);
    }

    [Fact]
    public async Task GetAsync_ForceRefresh_RechecksProvider()
    {
        var client = new StubTeseraClient
        {
            GameException = new TeseraUnavailableException("HTTP 403")
        };
        var service = CreateService(client);

        var first = await service.GetAsync(false, CancellationToken.None);
        client.GameException = null;
        client.GameResult = new ExternalGame(
            null,
            "carcassonne",
            "Каркассон",
            2,
            5,
            "3–4",
            "https://tesera.ru/game/carcassonne");
        var second = await service.GetAsync(true, CancellationToken.None);

        Assert.False(first.IsAvailable);
        Assert.True(second.IsAvailable);
        Assert.Equal(2, client.GameCalls);
    }

    private static TeseraAvailabilityService CreateService(ITeseraClient client)
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddSingleton(client);
        using var provider = services.BuildServiceProvider();

        return new TeseraAvailabilityService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IMemoryCache>(),
            NullLogger<TeseraAvailabilityService>.Instance);
    }

    private sealed class StubTeseraClient : ITeseraClient
    {
        public ExternalGame? GameResult { get; set; }
        public Exception? GameException { get; set; }
        public int GameCalls { get; private set; }

        public Task<IReadOnlyList<ExternalGame>> GetOwnedCollectionAsync(
            string username,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalGame>>([]);

        public Task<ExternalGame?> GetGameByAliasAsync(
            string alias,
            CancellationToken cancellationToken)
        {
            GameCalls++;
            if (GameException is not null)
            {
                return Task.FromException<ExternalGame?>(GameException);
            }

            return Task.FromResult(GameResult);
        }
    }
}
