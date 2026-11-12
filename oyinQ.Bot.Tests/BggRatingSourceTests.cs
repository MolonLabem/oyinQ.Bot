using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggRatingSourceTests
{
    [Theory]
    [InlineData("", false, null)]
    [InlineData("<usersrated value='0'/><average value='N/A'/>", true, null)]
    [InlineData("<usersrated value='20'/><average value='8.12'/>", true, "8.12")]
    [InlineData("<usersrated value='20'/><average value='11'/>", false, null)]
    [InlineData("<usersrated value='20'/>", false, null)]
    [InlineData("<usersrated value='many'/><average value='7'/>", false, null)]
    public void DistinguishesUnratedFromMalformedStatistics(string xml, bool valid, string? expected)
    {
        Assert.Equal(valid, BggRatingParser.TryRead(XElement.Parse($"<item><statistics><ratings>{xml}</ratings></statistics></item>"), out var value));
        Assert.Equal(expected, value?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task FetchesRatingsInOneBatchAndLeavesIncompleteItemsRetryable()
    {
        var handler = new RatingHandler();
        var client = new BoardGameGeekClient(new HttpClient(handler) { BaseAddress = new Uri("https://boardgamegeek.com") },
            Options.Create(new BggOptions { ApiToken = "test" }));
        var result = await client.GetRatingsAsync([42, 43, 44, 45, 42, 0], default);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(8.12m, result[42]); Assert.Null(result[43]);
        Assert.False(result.ContainsKey(44)); Assert.False(result.ContainsKey(45));
    }

    private sealed class RatingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("?id=42,43,44,45&stats=1", request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                <items>
                <item id="42"><statistics><ratings><usersrated value="20"/><average value="8.12"/></ratings></statistics></item>
                <item id="43"><statistics><ratings><usersrated value="0"/><average value="0"/></ratings></statistics></item>
                <item id="44"/>
                <item id="45"><statistics><ratings><usersrated value="3"/><average value="invalid"/></ratings></statistics></item>
                </items>
                """) });
        }
    }
}
