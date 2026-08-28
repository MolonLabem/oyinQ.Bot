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
                        <thumbnail>https://cf.geekdo-images.com/thumb.jpg</thumbnail>
                        <image>https://cf.geekdo-images.com/full.jpg</image>
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
        Assert.Equal("https://cf.geekdo-images.com/thumb.jpg", game.ThumbnailImageUrl);
        Assert.Equal("https://cf.geekdo-images.com/full.jpg", game.ImageUrl);
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

    [Fact]
    public async Task GetGameDetailsAsync_ReturnsOnlyReliablyLinkedInboundExpansions()
    {
        var handler = new StubHttpMessageHandler(_ => XmlResponse(
            HttpStatusCode.OK,
            """
            <items>
              <item type="boardgame" id="167791">
                <name type="primary" value="Terraforming Mars" />
                <minplayers value="1" />
                <maxplayers value="5" />
                <link type="boardgameexpansion" id="247030" value="Terraforming Mars: Prelude" inbound="true" />
                <link type="boardgameexpansion" id="231965" value="Terraforming Mars: Hellas &amp; Elysium" inbound="true" />
                <link type="boardgameexpansion" id="999" value="Unrelated outbound link" />
                <link type="boardgamecategory" id="1016" value="Science Fiction" inbound="true" />
              </item>
            </items>
            """));
        var client = CreateClient(handler);

        var details = await client.GetGameDetailsAsync(167791, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Terraforming Mars", details.Game.Name);
        Assert.Collection(
            details.Expansions,
            expansion => Assert.Equal(231965, expansion.BggId),
            expansion => Assert.Equal(247030, expansion.BggId));
    }

    [Fact]
    public async Task OwnedBaseGamesAndExpansions_UseSeparateRequests_AndMapParentLinks()
    {
        var collectionQueries = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/xmlapi2/collection")
            {
                collectionQueries.Add(request.RequestUri.Query);
                return request.RequestUri.Query.Contains("&subtype=boardgameexpansion&", StringComparison.Ordinal)
                    ? XmlResponse(HttpStatusCode.OK, """
                        <items><item objectid="20"><name>Expansion A</name><stats /></item></items>
                        """)
                    : XmlResponse(HttpStatusCode.OK, """
                        <items><item objectid="10"><name>Base A</name><stats minplayers="2" maxplayers="4" /></item></items>
                        """);
            }

            return request.RequestUri.Query.Contains("type=boardgameexpansion", StringComparison.Ordinal)
                ? XmlResponse(HttpStatusCode.OK, """
                    <items><item type="boardgameexpansion" id="20">
                      <name type="primary" value="Expansion A" />
                      <link type="boardgameexpansion" id="10" value="Base A" inbound="true" />
                      <link type="boardgameexpansion" id="30" value="Child Expansion" />
                    </item></items>
                    """)
                : XmlResponse(HttpStatusCode.OK, """
                    <items><item type="boardgame" id="10"><name type="primary" value="Base A" /></item></items>
                    """);
        });
        var client = CreateClient(handler);

        var bases = await client.GetOwnedBaseGamesAsync("owner", default);
        var expansions = await client.GetOwnedExpansionsAsync("owner", default);

        Assert.Equal(10, Assert.Single(bases).BggId);
        Assert.Equal(20, Assert.Single(expansions).Expansion.BggId);
        Assert.Equal([10L], expansions[0].ParentBggIds);
        Assert.Contains(collectionQueries, value => value.Contains("subtype=boardgame&", StringComparison.Ordinal));
        Assert.Contains(collectionQueries, value => value.Contains("subtype=boardgameexpansion", StringComparison.Ordinal));
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
