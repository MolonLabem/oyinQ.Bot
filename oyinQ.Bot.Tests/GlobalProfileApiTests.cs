using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

namespace oyinQ.Bot.Tests;

public sealed class GlobalProfileApiTests
{
    [Fact]
    public async Task AuthenticatedUserWithoutCommunitiesUsesGlobalProfileButCannotReadScopedSchedule()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var name = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton(Options.Create(new BotOptions { Token = "123:test" }));
        builder.Services.AddSingleton(Options.Create(new BggOptions()));
        builder.Services.AddScoped<TelegramMiniAppAuthenticator>(); builder.Services.AddScoped<ParticipantIdentityService>();
        builder.Services.AddScoped<PrivateChatCapability>(); builder.Services.AddScoped<ParticipantCollectionService>();
        builder.Services.AddScoped<ICommunityStore, CommunityStore>(); builder.Services.AddScoped<CommunityContextResolver>();
        builder.Services.AddSingleton<ICommunityMembershipVerifier, NoMembership>();
        builder.Services.AddScoped<GatheringPresentationService>(); builder.Services.AddScoped<CampBggImportCoordinator>();
        builder.Services.AddScoped<CampContributionSelectionService>(); builder.Services.AddScoped<CampParticipationPolicy>();
        builder.Services.AddScoped<GatheringPlayService>(); builder.Services.AddScoped<ExternalPlayReferenceService>();
        builder.Services.AddSingleton<IBoardGameGeekClient>(new NoBgg());
        using var botHttp = new HttpClient(new BotHandler());
        builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", botHttp));
        await using var app = builder.Build();
        var routes = app.MapGroup("/api/miniapp"); routes.AddEndpointFilter<MiniAppIdentityFilter>();
        routes.MapProfileEndpoints(); routes.MapProfileCollectionEndpoints(); routes.MapNotificationEndpoints(); routes.MapPlayEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/miniapp/profile")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/miniapp/profile/changelog")).StatusCode);
            client.DefaultRequestHeaders.Add("X-Telegram-Init-Data", SignedData());
            var changelog = await client.GetFromJsonAsync<JsonElement>("/api/miniapp/profile/changelog");
            Assert.Contains("# Изменения OyinQ", changelog.GetProperty("markdown").GetString());
            Assert.Contains("## 2026-09-04", changelog.GetProperty("markdown").GetString());
            var profile = await client.GetFromJsonAsync<JsonElement>("/api/miniapp/profile");
            Assert.True(profile.GetProperty("botStartRequired").GetBoolean());
            Assert.Contains("ActualBot?start=menu", profile.GetProperty("startUrl").GetString());
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/miniapp/profile", new { displayName = "Моё имя" })).StatusCode);
            Assert.Equal("Моё имя", (await client.GetFromJsonAsync<JsonElement>("/api/miniapp/profile")).GetProperty("preferredDisplayName").GetString());
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var me = await db.Participants.SingleAsync();
                await new ParticipantCollectionService(db).UpsertAsync(me.Id,
                    [new(42, CollectionItemType.BaseGame, null, new(1, "Моя игра", null, null, 1, 4, null))], CollectionItemSource.Manual, DateTimeOffset.UtcNow, default);
                var other = await new ParticipantIdentityService(db, TimeProvider.System).GetOrCreateAsync(99, null, "Другой", null, default);
                await new ParticipantCollectionService(db).UpsertAsync(other.Id,
                    [new(43, CollectionItemType.BaseGame, null, new(1, "Чужая игра", null, null, 1, 4, null))], CollectionItemSource.Manual, DateTimeOffset.UtcNow, default);
                db.OyinQCommunities.Add(new() { Key = "private", Mode = BotMode.Club, Name = "Закрытый клуб", TimeZoneId = "UTC", IsActive = true });
                db.GameGatherings.Add(GatheringRules.Create("private", new(GatheringGameSnapshot.CurrentVersion, 42, "Скрытый сбор", null, null, 1, 4, null, [], "bgg", []),
                    me.Id, DateTimeOffset.UtcNow.AddDays(1), 1, 2, 4, null, false, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }
            var collection = await client.GetFromJsonAsync<JsonElement>("/api/miniapp/profile/collection/");
            Assert.Equal(42, Assert.Single(collection.EnumerateArray()).GetProperty("bggId").GetInt64());
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/miniapp/profile/gatherings")).EnumerateArray());
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/miniapp/profile/plays")).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/miniapp/profile/notifications")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/miniapp/profile/collection/BaseGame/42")).StatusCode);
        }
        finally { await app.StopAsync(); }
    }
    private static string SignedData()
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        { ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ["user"] = "{\"id\":42,\"first_name\":\"Игрок\"}" };
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes("123:test"));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', values.Select(x => $"{x.Key}={x.Value}"))));
        return string.Join('&', values.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")) + "&hash=" + Convert.ToHexStringLower(hash);
    }
    private sealed class NoMembership : ICommunityMembershipVerifier
    { public Task<bool> IsMemberAsync(long chat, long user, CancellationToken ct) => Task.FromResult(false); }
    private sealed class BotHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("""{"ok":true,"result":{"id":123456,"is_bot":true,"first_name":"OyinQ","username":"ActualBot"}}""", Encoding.UTF8, "application/json") });
    }
    private sealed class NoBgg : IBoardGameGeekClient
    {
        public Task<BggGameDetails?> GetGameDetailsAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggBaseGameSearchResult>> SearchAsync(string q, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<oyinQ.Bot.Integrations.ExternalGame>> GetOwnedBaseGamesAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => throw new NotSupportedException();
    }
}
