using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BoardGameGeekClientTests
{
    [Fact]
    public void SearchRanking_PrefersExactPrefixWholeWordAndContains()
    {
        var ranked = BoardGameGeekClient.RankSearchResults([
            new(1, "Nightmars", 2020),
            new(2, "Mars Colony", 2023),
            new(3, "Terraforming Mars", 2019),
            new(4, "Mars", 2016),
            new(5, "Red Marshlands", 2022)], "Mars");

        Assert.Equal([4L, 2L, 3L, 5L, 1L], ranked.Select(x => x.BggId));
    }

    [Fact]
    public async Task SearchAsync_EncodesQuery_AndReturnsUpToTwentyFivePrimaryNames()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/xmlapi2/search")
            {
                Assert.Contains("query=Terraforming%20Mars", request.RequestUri.Query, StringComparison.Ordinal);
                Assert.Contains("type=boardgame", request.RequestUri.Query, StringComparison.Ordinal);
                return XmlResponse(HttpStatusCode.OK, """
                <items>
                  <item id="1"><name type="alternate" value="Ignore me" /><name type="primary" value="Game 1" /><yearpublished value="2020" /></item>
                  <item id="2"><name type="primary" value="Game 2" /></item>
                  <item id="3"><name type="primary" value="Game 3" /></item>
                  <item id="4"><name type="primary" value="Game 4" /></item>
                  <item id="5"><name type="primary" value="Game 5" /></item>
                  <item id="6"><name type="primary" value="Game 6" /></item>
                </items>
                """);
            }

            Assert.Equal("/xmlapi2/thing", request.RequestUri.AbsolutePath);
            Assert.Contains("versions=1", request.RequestUri.Query, StringComparison.Ordinal);
            return XmlResponse(HttpStatusCode.OK, """
                <items>
                  <item type="boardgame" id="1"><name type="primary" value="Game 1" /><yearpublished value="2020" />
                    <versions><item><name type="primary" value="Russian edition" /><canonicalname value="Игра 1" /><link type="language" id="2202" value="Russian" /></item></versions>
                  </item>
                  <item type="boardgame" id="2"><name type="primary" value="Game 2" /></item>
                  <item type="boardgame" id="3"><name type="primary" value="Game 3" /></item>
                  <item type="boardgame" id="4"><name type="primary" value="Game 4" /></item>
                  <item type="boardgame" id="5"><name type="primary" value="Game 5" /></item>
                  <item type="boardgame" id="6"><name type="primary" value="Game 6" /></item>
                </items>
                """);
        });

        var games = await CreateClient(handler).SearchAsync("  Terraforming Mars  ", default);

        Assert.Equal(6, games.Count);
        Assert.Equal(new[] { "Игра 1", "Game 2", "Game 3", "Game 4", "Game 5", "Game 6" },
            games.Select(game => game.Name));
        Assert.Equal("Game 1", games[0].OriginalName);
        Assert.Equal(2020, games[0].YearPublished);
    }

    [Fact]
    public async Task GetOwnedBaseGamesAsync_RequestsBaseGames_AndRejectsExpansionThing()
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

        var games = await client.GetOwnedBaseGamesAsync("test-user", CancellationToken.None);

        var game = Assert.Single(games);
        Assert.Equal(1, game.BggId);
        Assert.Equal("Base Game", game.Name);
        Assert.Equal("https://cf.geekdo-images.com/thumb.jpg", game.ThumbnailImageUrl);
        Assert.Equal("https://cf.geekdo-images.com/full.jpg", game.ImageUrl);
        Assert.DoesNotContain(games, value => value.BggId == 2);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task GetGameDetailsAsync_ReturnsOnlyReliablyLinkedInboundExpansions()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Contains("versions=1", request.RequestUri!.Query, StringComparison.Ordinal);
            if (!request.RequestUri.Query.Contains("id=167791", StringComparison.Ordinal))
                return XmlResponse(HttpStatusCode.OK, """
                    <items>
                      <item type="boardgameexpansion" id="247030">
                        <name type="primary" value="Terraforming Mars: Prelude" />
                        <versions><item><canonicalname value="Покорение Марса: Пролог" /><link type="language" id="2202" value="Russian" /></item></versions>
                      </item>
                      <item type="boardgameexpansion" id="231965">
                        <name type="primary" value="Terraforming Mars: Hellas &amp; Elysium" />
                      </item>
                    </items>
                    """);
            return XmlResponse(HttpStatusCode.OK, """
            <items>
              <item type="boardgame" id="167791">
                <name type="primary" value="Terraforming Mars" />
                <description>First paragraph.&amp;#10;&amp;#10;&lt;b&gt;Second paragraph&lt;/b&gt;</description>
                <yearpublished value="2016" />
                <minplayers value="1" />
                <maxplayers value="5" />
                <minplaytime value="90" /><maxplaytime value="120" /><minage value="12" />
                <link type="boardgamesubdomain" id="5497" value="Strategy Games" />
                <link type="boardgamecategory" id="1021" value="Economic" />
                <link type="boardgamemechanic" id="2040" value="Hand Management" />
                <link type="boardgameexpansion" id="247030" value="Terraforming Mars: Prelude" inbound="true" />
                <link type="boardgameexpansion" id="231965" value="Terraforming Mars: Hellas &amp; Elysium" inbound="true" />
                <link type="boardgameexpansion" id="999" value="Unrelated outbound link" />
                <link type="boardgamecategory" id="1016" value="Science Fiction" inbound="true" />
              </item>
            </items>
            """);
        });
        var client = CreateClient(handler);

        var details = await client.GetGameDetailsAsync(167791, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Terraforming Mars", details.Game.Name);
        Assert.Equal(["Strategy"], details.Game.Types);
        Assert.Equal(["Economic", "Science Fiction"], details.Game.Categories);
        Assert.Equal("First paragraph.\n\nSecond paragraph", details.Game.Description);
        Assert.Equal(2016, details.Game.YearPublished);
        Assert.Equal(90, details.Game.MinPlayTimeMinutes);
        Assert.Equal(120, details.Game.MaxPlayTimeMinutes);
        Assert.Equal(12, details.Game.MinAge);
        Assert.Equal(oyinQ.Bot.Features.Collections.GameType.Strategy, details.Game.Type);
        Assert.Equal(1021, details.Game.CategoryItems!.First().BggId);
        Assert.Equal(2040, Assert.Single(details.Game.Mechanics!).BggId);
        Assert.Collection(
            details.Expansions,
            expansion => Assert.Equal(231965, expansion.BggId),
            expansion =>
            {
                Assert.Equal(247030, expansion.BggId);
                Assert.Equal("Покорение Марса: Пролог", expansion.Name);
                Assert.Equal("Terraforming Mars: Prelude", expansion.OriginalName);
            });
    }

    [Fact]
    public async Task GetGameDetailsAsync_ReadsSubdomainsFromCurrentRankPayload()
    {
        var handler = new StubHttpMessageHandler(_ => XmlResponse(HttpStatusCode.OK, """
            <items><item type="boardgame" id="170561">
              <name type="primary" value="Valeria: Card Kingdoms" />
              <link type="boardgamecategory" id="1002" value="Card Game" />
              <statistics><ratings><ranks>
                <rank type="subtype" id="1" name="boardgame" friendlyname="Board Game Rank" value="672" />
                <rank type="family" id="5497" name="strategygames" friendlyname="Strategy Game Rank" value="437" />
                <rank type="family" id="5499" name="familygames" friendlyname="Family Game Rank" value="152" />
              </ranks></ratings></statistics>
            </item></items>
            """));
        var client = CreateClient(handler);

        var details = await client.GetGameDetailsAsync(170561, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal([5499L, 5497L], details.Game.Subdomains!.Select(value => value.BggId));
        Assert.Equal([GameType.Family, GameType.Strategy],
            BggTaxonomyCatalog.MapGameTypes(details.Game.Subdomains!));
        Assert.Equal(GameType.Family, details.Game.Type);
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

    [Fact]
    public async Task GetItemsByIdsAsync_UsesSingleMixedTypeRequestAndOfficialExpansionParents()
    {
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/xmlapi2/thing", request.RequestUri!.AbsolutePath);
            Assert.Contains("id=10,20", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
            requests.Add(request.RequestUri.Query);
            Assert.DoesNotContain("type=", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("versions=1", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
            return XmlResponse(HttpStatusCode.OK, """
                  <items>
                  <item type="boardgame" id="10">
                    <name type="primary" value="Base" />
                    <minplayers value="2" /><maxplayers value="4" />
                  </item>
                  <item type="boardgameexpansion" id="20">
                    <name type="primary" value="Expansion" />
                    <link type="boardgameexpansion" id="10" value="Base" inbound="true" />
                    <link type="boardgameexpansion" id="30" value="Child" />
                  </item>
                  </items>
                  """);
        });

        var items = await CreateClient(handler).GetItemsByIdsAsync([10, 10, 20], default);

        Assert.False(items.Single(item => item.Game.BggId == 10).IsExpansion);
        var expansion = items.Single(item => item.Game.BggId == 20);
        Assert.True(expansion.IsExpansion);
        Assert.Equal([10L], expansion.ParentBggIds);
        Assert.Single(requests);
    }

    [Fact]
    public async Task GetItemsByIdsAsync_NeverSendsMoreThanTwentyIdsPerThingRequest()
    {
        var batchSizes = new List<int>();
        var handler = new StubHttpMessageHandler(request =>
        {
            var query = request.RequestUri!.Query.TrimStart('?').Split('&')
                .Single(x => x.StartsWith("id=", StringComparison.Ordinal));
            var ids = Uri.UnescapeDataString(query[3..]).Split(',', StringSplitOptions.RemoveEmptyEntries);
            batchSizes.Add(ids.Length);
            return XmlResponse(HttpStatusCode.OK, "<items />");
        });

        await CreateClient(handler).GetItemsByIdsAsync(Enumerable.Range(1, 21).Select(x => (long)x).ToArray(),
            default);

        Assert.Equal([20, 1], batchSizes);
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
