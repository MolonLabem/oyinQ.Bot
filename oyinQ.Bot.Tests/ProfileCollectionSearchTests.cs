using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class ProfileCollectionSearchTests
{
    [Theory]
    [InlineData("134342")]
    [InlineData("https://boardgamegeek.com/boardgame/134342/lords-of-waterdeep-scoundrels-of-skullport")]
    public async Task ExpansionFromSearchOrLink_CanBePreviewedAndAddedWithoutOwningBase(string input)
    {
        await using var fixture = await Fixture.CreateAsync();
        var search = await fixture.Client.GetFromJsonAsync<JsonElement>("/api/miniapp/bgg/search?query=Scoundrels");
        Assert.Equal(134342, Assert.Single(search.EnumerateArray()).GetProperty("bggId").GetInt64());

        var path = $"/api/miniapp/bgg/game?input={Uri.EscapeDataString(input)}";
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync(path)).StatusCode);
        var preview = await fixture.Client.GetFromJsonAsync<JsonElement>(path + "&allowExpansions=true");
        Assert.Equal("Expansion", preview.GetProperty("game").GetProperty("itemType").GetString());
        Assert.Equal(134342, preview.GetProperty("game").GetProperty("bggId").GetInt64());
        Assert.Empty(preview.GetProperty("expansions").EnumerateArray());
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.ParticipantCollectionItems.ToArrayAsync());

        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.PostAsJsonAsync(
                "/api/miniapp/profile/collection/manual", new { bggInput = input, expansionBggIds = Array.Empty<long>() })).StatusCode);

        var row = Assert.Single(await db.ParticipantCollectionItems.ToArrayAsync());
        Assert.Equal(134342, row.BggId);
        Assert.Equal(CollectionItemType.Expansion, row.ItemType);
        Assert.Equal(CollectionItemSource.Manual, row.Source);
        Assert.Equal([110327L], row.ReadSnapshot().ParentBggIds);
        Assert.Null(row.ReadSnapshot().MinAge);
        Assert.Equal(60, row.ReadSnapshot().MinPlayTimeMinutes);
        Assert.Equal(120, row.ReadSnapshot().MaxPlayTimeMinutes);
    }

    [Fact]
    public async Task BaseAndExpansionSelection_StillRequiresOfficialParentRelationship()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Client.GetFromJsonAsync<JsonElement>("/api/miniapp/bgg/game?input=110327");
        Assert.Equal("BaseGame", preview.GetProperty("game").GetProperty("itemType").GetString());
        Assert.Equal(134342, Assert.Single(preview.GetProperty("expansions").EnumerateArray()).GetProperty("bggId").GetInt64());
        var rejected = await fixture.Client.PostAsJsonAsync("/api/miniapp/profile/collection/manual",
            new { bggInput = "110327", expansionBggIds = new[] { 999L } });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.ParticipantCollectionItems.ToArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.PostAsJsonAsync(
            "/api/miniapp/profile/collection/manual", new { bggInput = "110327", expansionBggIds = new[] { 134342L } })).StatusCode);
        var rows = await db.ParticipantCollectionItems.ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row => row.BggId == 110327 && row.ItemType == CollectionItemType.BaseGame);
        Assert.Contains(rows, row => row.BggId == 134342 && row.ItemType == CollectionItemType.Expansion);
    }

    [Fact]
    public async Task ExpansionPreview_RequiresAuthenticationAndRejectsUnsupportedItems()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync(
            "/api/miniapp/bgg/game?input=999&allowExpansions=true")).StatusCode);
        fixture.Client.DefaultRequestHeaders.Remove("X-Telegram-Init-Data");
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync(
            "/api/miniapp/bgg/game?input=134342&allowExpansions=true")).StatusCode);
    }

    private sealed class Fixture(WebApplication app, HttpClient client, HttpClient bggHttp) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;
        public HttpClient Client { get; } = client;

        public static async Task<Fixture> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var database = Guid.NewGuid().ToString();
            builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(database));
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddSingleton(Options.Create(new BotOptions { Token = "123:test" }));
            var bggOptions = Options.Create(new BggOptions { ApiToken = "fake-token" });
            builder.Services.AddSingleton(bggOptions);
            builder.Services.AddScoped<TelegramMiniAppAuthenticator>();
            builder.Services.AddScoped<ParticipantIdentityService>();
            builder.Services.AddScoped<ParticipantCollectionService>();
            builder.Services.AddScoped<CampBggImportCoordinator>();
            builder.Services.AddScoped<CampContributionSelectionService>();
            builder.Services.AddScoped<CampParticipationPolicy>();
            var bggHttp = new HttpClient(new BggHandler()) { BaseAddress = new Uri("https://boardgamegeek.com") };
            builder.Services.AddSingleton<IBoardGameGeekClient>(new BoardGameGeekClient(bggHttp, bggOptions));
            var app = builder.Build();
            var routes = app.MapGroup("/api/miniapp");
            routes.AddEndpointFilter<MiniAppIdentityFilter>();
            routes.MapBggEndpoints();
            routes.MapProfileCollectionEndpoints();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient { BaseAddress = new Uri(address) };
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ["user"] = "{\"id\":42,\"first_name\":\"Игрок\"}"
            };
            var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes("123:test"));
            var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', values.Select(x => $"{x.Key}={x.Value}"))));
            client.DefaultRequestHeaders.Add("X-Telegram-Init-Data",
                string.Join('&', values.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")) + "&hash=" + Convert.ToHexStringLower(hash));
            return new(app, client, bggHttp);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            bggHttp.Dispose();
            await App.DisposeAsync();
        }
    }

    // Search can label an expansion as boardgame, while typed /thing correctly excludes it.
    private sealed class BggHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
                return Xml("<items><item type=\"boardgame\" id=\"134342\"><name type=\"primary\" value=\"Lords of Waterdeep: Scoundrels of Skullport\" /></item></items>");
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            var ids = query["id"].ToString().Split(',');
            var items = new List<string>();
            if (ids.Contains("110327")) items.Add("""
                <item type="boardgame" id="110327"><name type="primary" value="Lords of Waterdeep" />
                <minplayers value="2" /><maxplayers value="5" />
                <link type="boardgameexpansion" id="134342" value="Scoundrels of Skullport" inbound="true" /></item>
                """);
            if (ids.Contains("134342") && (!query.TryGetValue("type", out var type) || type != "boardgame")) items.Add("""
                <item type="boardgameexpansion" id="134342"><name type="primary" value="Lords of Waterdeep: Scoundrels of Skullport" />
                <minplayers value="2" /><maxplayers value="6" />
                <minplaytime value="120" /><maxplaytime value="60" /><minage value="200" />
                <link type="boardgameexpansion" id="110327" value="Lords of Waterdeep" inbound="true" /></item>
                """);
            return Xml("<items>" + string.Concat(items) + "</items>");
        }

        private static Task<HttpResponseMessage> Xml(string text) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(text, Encoding.UTF8, "application/xml") });
    }
}
