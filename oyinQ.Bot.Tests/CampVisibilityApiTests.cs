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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class CampVisibilityApiTests
{
    [Fact]
    public async Task RealRoutesShareDefaultsAndEnforceHidingRegistrationAndMembership()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var database = Guid.NewGuid().ToString(); var clock = new PlanningClock(); var membership = new Membership();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(database).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(Options.Create(new BotOptions { Token = "123:test" }));
        builder.Services.AddScoped<TelegramMiniAppAuthenticator>(); builder.Services.AddScoped<ParticipantIdentityService>();
        builder.Services.AddScoped<ICommunityStore, CommunityStore>(); builder.Services.AddScoped<CommunityContextResolver>();
        builder.Services.AddSingleton<ICommunityMembershipVerifier>(membership);
        builder.Services.AddScoped<CampParticipationPolicy>(); builder.Services.AddScoped<CampContributionSelectionService>();
        builder.Services.AddScoped<CampWishlistService>(); builder.Services.AddScoped<GameCatalogService>(); builder.Services.AddScoped<EffectiveCampCatalogService>();
        await using var app = builder.Build();
        var routes = app.MapGroup("/api/miniapp"); routes.AddEndpointFilter<MiniAppIdentityFilter>();
        routes.MapCampWishlistEndpoints(); routes.MapCatalogEndpoints();
        CampWishSeed seed;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); seed = await CampWishSeed.Create(db, clock);
            db.ParticipantCollectionItems.Add(CampWishSeed.Owned(seed.Owner, 99));
            db.GameWishes.Add(new() { ParticipantId = seed.A.Id, CommunityKey = "another-camp", BggId = 999,
                SnapshotJson = seed.A.Id.ToString() }); // Must never be read/deserialized in this Camp.
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            const string query = "?community=camp-wishes";
            void Login(long user)
            {
                client.DefaultRequestHeaders.Remove("X-Telegram-Init-Data");
                client.DefaultRequestHeaders.Add("X-Telegram-Init-Data", SignedData(user, clock));
            }
            Task<JsonElement> Get(string suffix) => client.GetFromJsonAsync<JsonElement>("/api/miniapp/camp-wishlist" + query + suffix);
            string[] urls = ["/camp-wishlist" + query, "/camp-wishlist" + query + "&view=settings",
                "/camp-wishlist" + query + "&game=99", "/camp-wishlist" + query + "&view=profile&person=" + seed.Owner.PublicId,
                "/catalog/demand/99" + query];
            foreach (var url in urls) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/miniapp" + url)).StatusCode);
            Login(seed.A.TelegramUserId);
            Assert.True((await Get("&view=settings")).GetProperty("shareCollection").GetBoolean());
            Assert.True((await Get("")).GetProperty("shareWishes").GetBoolean());
            Assert.Equal(1, (await Get("")).GetProperty("total").GetInt32());
            Assert.Equal("owned", (await Get("&game=99")).GetProperty("owners")[0].GetProperty("status").GetString());
            Assert.Equal(seed.A.PublicId, (await Get("&game=42")).GetProperty("interested")[0].GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/miniapp/catalog/demand/99" + query)).StatusCode);
            Login(seed.Owner.TelegramUserId);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/miniapp/camp-wishlist" + query,
                new { action = "privacy", shareCollection = false })).StatusCode);
            Login(seed.A.TelegramUserId);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/miniapp/camp-wishlist" + query,
                new { action = "privacy", shareWishes = false })).StatusCode);
            Assert.Equal(0, (await Get("&view=profile&person=" + seed.Owner.PublicId)).GetProperty("total").GetInt32());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/miniapp/camp-wishlist" + query + "&game=99")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/miniapp/catalog/demand/99" + query)).StatusCode);
            Login(seed.Owner.TelegramUserId);
            Assert.Equal(0, (await Get("&game=42")).GetProperty("interested").GetArrayLength());
            Assert.Equal(1, (await Get("&game=42")).GetProperty("anonymousCount").GetInt32());
            Assert.False((await Get("&view=profile&mode=wishes&person=" + seed.A.PublicId)).GetProperty("wishesVisible").GetBoolean());
            // Knowing IDs is insufficient: registration and current community access are independently required.
            Login(999999);
            foreach (var url in urls) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/miniapp" + url)).StatusCode);
            Login(seed.A.TelegramUserId); membership.Allowed = false;
            foreach (var url in urls) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/miniapp" + url)).StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    private sealed class Membership : ICommunityMembershipVerifier
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> IsMemberAsync(long chat, long user, CancellationToken ct) => Task.FromResult(Allowed);
    }

    private static string SignedData(long id, TimeProvider clock)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        { ["auth_date"] = clock.GetUtcNow().ToUnixTimeSeconds().ToString(), ["user"] = JsonSerializer.Serialize(new { id, first_name = "Участник" }) };
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes("123:test"));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', values.Select(x => $"{x.Key}={x.Value}"))));
        return string.Join('&', values.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")) + "&hash=" + Convert.ToHexStringLower(hash);
    }
}
