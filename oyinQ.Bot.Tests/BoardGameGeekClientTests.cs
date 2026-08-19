using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BoardGameGeekClientTests
{
    [Fact]
    public async Task GetOwnedCollectionAsync_RequestsBaseGames_AndRejectsExpansionThing()
    {
        var requests = new List<Uri>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);

            if (request.RequestUri!.AbsolutePath == "/xmlapi2/collection")
            {
                Assert.Contains("subtype=boardgame", request.RequestUri.Query, StringComparison.Ordinal);
                Assert.Contains("excludesubtype=boardgameexpansion", request.RequestUri.Query, StringComparison.Ordinal);
                return XmlResponse(
                    HttpStatusCode.OK,
                    """
                    <items totalitems="2">
                      <item objecttype="thing" objectid="1" subtype="boardgame">
                        <name>Base Game</name>
                        <stats minplayers="2" maxplayers="4" />
                      </item>
                      <item objecttype="thing" objectid="2" subtype="boardgame">
                        <name>Expansion mislabeled by collection</name>
                        <stats minplayers="2" maxplayers="4" />
                      </item>
                    </items>
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/xmlapi2/thing")
            {
                Assert.Contains("type=boardgame", request.RequestUri.Query, StringComparison.Ordinal);
                return XmlResponse(
                    HttpStatusCode.OK,
                    """
                    <items>
                      <item type="boardgame" id="1">
                        <name type="primary" value="Base Game" />
                        <minplayers value="2" />
                        <maxplayers value="4" />
                      </item>
                      <item type="boardgameexpansion" id="2">
                        <name type="primary" value="Expansion" />
                        <minplayers value="2" />
                        <maxplayers value="4" />
                      </item>
                    </items>
                    """);
            }

            return XmlResponse(HttpStatusCode.NotFound, "<items />");
        });

        var client = CreateClient(handler);

        var games = await client.GetOwnedCollectionAsync("test-user", CancellationToken.None);

        var game = Assert.Single(games);
        Assert.Equal(1, game.BggId);
        Assert.Equal("Base Game", game.Name);
        Assert.DoesNotContain(games, value => value.BggId == 2);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task GetGameAsync_WhenBggClassifiesItemAsExpansion_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/xmlapi2/thing", request.RequestUri!.AbsolutePath);
            Assert.Contains("type=boardgame", request.RequestUri.Query, StringComparison.Ordinal);
            return XmlResponse(
                HttpStatusCode.OK,
                """
                <items>
                  <item type="boardgameexpansion" id="2">
                    <name type="primary" value="Expansion" />
                  </item>
                </items>
                """);
        });

        var client = CreateClient(handler);

        var game = await client.GetGameAsync(2, CancellationToken.None);

        Assert.Null(game);
    }

    private static BoardGameGeekClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://boardgamegeek.com")
        };
        return new BoardGameGeekClient(
            httpClient,
            Options.Create(new BggOptions { ApiToken = "test-token" }));
    }

    private static HttpResponseMessage XmlResponse(HttpStatusCode statusCode, string xml)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml")
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
