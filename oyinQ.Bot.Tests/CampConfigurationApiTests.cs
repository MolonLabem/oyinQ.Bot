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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Tests;

public sealed class CampConfigurationApiTests
{
    [Fact]
    public async Task ActualRegistrationRoutesCalculateSaveReadAndProtectPrivateAnswers()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var clock = new CampConfigurationTests.Clock(); var membership = new Membership(); var name = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddSingleton<TimeProvider>(clock); builder.Services.AddSingleton(Options.Create(new BotOptions { Token = "123:test" }));
        builder.Services.AddSingleton(Options.Create(new AdministrationOptions { SuperAdminTelegramUserIds = new HashSet<long> { 42 } }));
        builder.Services.AddSingleton<ITelegramChatAdministratorVerifier>(new Administrators());
        builder.Services.AddScoped<IAdminAuthorizationService, AdminAuthorizationService>();
        builder.Services.AddScoped<TelegramMiniAppAuthenticator>(); builder.Services.AddScoped<ParticipantIdentityService>();
        builder.Services.AddScoped<ICommunityStore, CommunityStore>(); builder.Services.AddScoped<CommunityContextResolver>();
        builder.Services.AddSingleton<ICommunityMembershipVerifier>(membership); builder.Services.AddScoped<CampRegistrationService>();
        builder.Services.AddScoped(provider => new GatheringPublicationService(provider.GetRequiredService<AppDbContext>(),
            provider.GetRequiredService<ICommunityStore>(), null!, clock, NullLogger<GatheringPublicationService>.Instance));
        await using var app = builder.Build(); var routes = app.MapGroup("/api/miniapp"); routes.AddEndpointFilter<MiniAppIdentityFilter>(); routes.MapCampRegistrationEndpoints(); routes.MapGroup("/admin").MapCampPricingPreviewEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Camps.Add(new() { Name = "Хэллоуин", BotChat = new() { Key = "camp", Name = "Хэллоуин", Mode = BotMode.Camp, TelegramChatId = -1001234, TimeZoneId = "Asia/Qyzylorda", IsActive = true },
                StartsAtUtc = DateTimeOffset.Parse("2026-10-31T07:00:00Z"), EndsAtUtc = DateTimeOffset.Parse("2026-11-01T18:00:00Z"), Status = CampStatus.Active,
                ConfigurationJson = CampConfigurationRules.Serialize(CampConfigurationTests.Configuration()) }); await db.SaveChangesAsync();
        }
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            const string path = "/api/miniapp/camp/registration";
            var choices = new { communityKey = "camp", selectedDates = new[] { "2026-10-31", "2026-11-01" }, answers = CampConfigurationTests.Answers(), city = "Алматы", needsAccommodation = true, displayName = "Игрок", total = 1 };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path + "/quote", choices)).StatusCode);
            void Login(long actor) { client.DefaultRequestHeaders.Remove("X-Telegram-Init-Data"); client.DefaultRequestHeaders.Add("X-Telegram-Init-Data", Signed(actor, clock)); }
            Login(42);
            const string adminPreview = "/api/miniapp/admin/camps/pricing-preview";
            var pricingExample = new { configuration = CampConfigurationTests.Configuration(), days = 2, needsAccommodation = false,
                answers = CampConfigurationTests.Answers(), timeZoneId = "Asia/Qyzylorda", registrationDate = "2026-10-27", total = 1 };
            var exampleResponse = await client.PostAsJsonAsync(adminPreview, pricingExample);
            Assert.Equal(HttpStatusCode.OK, exampleResponse.StatusCode);
            Assert.Equal("no-store", exampleResponse.Headers.CacheControl!.ToString());
            var example = await exampleResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(11000, example.GetProperty("quote").GetProperty("total").GetDecimal());
            Assert.Equal(3, example.GetProperty("quote").GetProperty("lines").GetArrayLength());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(adminPreview,
                new { configuration = CampConfigurationTests.Configuration(), days = 0, needsAccommodation = false, timeZoneId = "UTC" })).StatusCode);
            await using (var scope = app.Services.CreateAsyncScope())
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().CampRegistrations.ToArrayAsync());
            var preview = await (await client.PostAsJsonAsync(path + "/quote", choices)).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(11000, preview.GetProperty("quote").GetProperty("total").GetDecimal());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, new { communityKey = "camp", selectedDates = new[] { "2026-10-31" }, city = "Алматы", needsAccommodation = false })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path, choices)).StatusCode);
            var saved = await client.GetFromJsonAsync<JsonElement>(path + "?community=camp");
            var reg = saved.GetProperty("registration"); Assert.True(reg.GetProperty("registered").GetBoolean());
            Assert.Equal(11000, reg.GetProperty("data").GetProperty("quote").GetProperty("total").GetDecimal());
            Assert.Equal("evening", reg.GetProperty("data").GetProperty("answers").GetProperty("arrival").GetString());
            Assert.Equal("Дом", saved.GetProperty("configuration").GetProperty("locationName").GetString());
            Login(43);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(adminPreview, pricingExample)).StatusCode);
            var other = await client.GetFromJsonAsync<JsonElement>(path + "?community=camp&participantId=42");
            Assert.Equal(JsonValueKind.Null, other.GetProperty("registration").ValueKind);
            membership.Allowed = false;
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path + "?community=camp")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path + "/quote", choices)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path, choices)).StatusCode);
        }
        finally { await app.StopAsync(); }
    }
    private static string Signed(long actor, TimeProvider clock)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["auth_date"] = clock.GetUtcNow().ToUnixTimeSeconds().ToString(), ["user"] = JsonSerializer.Serialize(new { id = actor, first_name = "Игрок" }) };
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes("123:test"));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', values.Select(x => $"{x.Key}={x.Value}"))));
        return string.Join('&', values.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")) + "&hash=" + Convert.ToHexString(hash);
    }
    private sealed class Administrators : ITelegramChatAdministratorVerifier
    {
        public Task<bool> IsAdministratorAsync(long chatId, long userId, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(long chatId, CancellationToken ct) => Task.FromResult<IReadOnlyList<EligibleGroupAdministrator>>([]);
    }
    private sealed class Membership : ICommunityMembershipVerifier
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> IsMemberAsync(long chatId, long userId, CancellationToken ct) => Task.FromResult(Allowed);
    }
}
