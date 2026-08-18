using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Integrations.Tesera;

namespace oyinQ.Bot.Tests;

public sealed class TeseraClientTests
{
    [Fact]
    public async Task GetOwnedCollectionAsync_UsesFallbackEndpoint_FiltersAdditions_AndParsesGameDetails()
    {
        var requests = new List<Uri>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);

            if (request.RequestUri!.AbsolutePath == "/collections/base/own/test-user")
            {
                Assert.Equal("?GamesType=SelfGame&Limit=100&Offset=0", request.RequestUri.Query);
                return JsonResponse(HttpStatusCode.OK, "{\"games\":[]}");
            }

            if (request.RequestUri.AbsolutePath == "/collections/base/Own/test-user")
            {
                Assert.Equal("?GamesType=SelfGame&Limit=100&Offset=0", request.RequestUri.Query);
                return JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "games": [
                        { "game": { "alias": "base-game", "isAddition": false } },
                        { "Game": { "Alias": "expansion", "IsAddition": 1 } }
                      ]
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/games/base-game")
            {
                Assert.Contains(
                    request.Headers.Accept,
                    value => value.MediaType == "application/json");
                return JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "Game": {
                        "Alias": "base-game",
                        "TitleRus": "Базовая игра",
                        "PlayersMin": 2,
                        "PlayersMax": 5,
                        "PlayersMinRecommend": 3,
                        "PlayersMaxRecommend": 4
                      }
                    }
                    """);
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });

        var client = CreateClient(handler);

        var games = await client.GetOwnedCollectionAsync("test-user", CancellationToken.None);

        var game = Assert.Single(games);
        Assert.Null(game.BggId);
        Assert.Equal("base-game", game.TeseraAlias);
        Assert.Equal("Базовая игра", game.Name);
        Assert.Equal(2, game.MinPlayers);
        Assert.Equal(5, game.MaxPlayers);
        Assert.Equal("3–4", game.BestPlayers);
        Assert.Equal("https://tesera.ru/game/base-game", game.ExternalUrl);
        Assert.DoesNotContain(requests, uri => uri.AbsolutePath == "/games/expansion");
        Assert.Equal(
            new[]
            {
                "/collections/base/own/test-user",
                "/collections/base/Own/test-user",
                "/games/base-game"
            },
            requests.Select(uri => uri.AbsolutePath).ToArray());
    }

    [Fact]
    public async Task GetGameByAliasAsync_RetriesFailedDetailRequest()
    {
        var detailAttempts = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/games/retry-game", request.RequestUri!.AbsolutePath);
            detailAttempts++;

            return detailAttempts == 1
                ? JsonResponse(HttpStatusCode.InternalServerError, "{}")
                : JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "alias": "retry-game",
                      "title": "Retry Game",
                      "playersMin": 1,
                      "playersMax": 4,
                      "playersMinRecommend": 2,
                      "playersMaxRecommend": 2
                    }
                    """);
        });

        var client = CreateClient(handler);

        var game = await client.GetGameByAliasAsync("retry-game", CancellationToken.None);

        Assert.NotNull(game);
        Assert.Equal(2, detailAttempts);
        Assert.Equal("retry-game", game.TeseraAlias);
        Assert.Equal("2", game.BestPlayers);
    }

    [Fact]
    public async Task GetOwnedCollectionAsync_WhenAllVariantsAreUnauthorized_ThrowsUnavailable()
    {
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return JsonResponse(HttpStatusCode.Unauthorized, "{}");
        });

        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<TeseraUnavailableException>(
            () => client.GetOwnedCollectionAsync("test-user", CancellationToken.None));

        Assert.Equal(4, requestCount);
        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
    }

    private static TeseraClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.tesera.ru")
        };

        return new TeseraClient(httpClient, NullLogger<TeseraClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        return response;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
