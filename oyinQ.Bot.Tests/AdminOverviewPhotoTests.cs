using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

namespace oyinQ.Bot.Tests;

public sealed class AdminOverviewPhotoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverviewUsesSharedPhotosOnlyForManageableCommunities(bool superAdmin)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        for (var index = 1; index <= 4; index++)
        {
            var key = $"community-{index}";
            var mode = index == 2 ? BotMode.Camp : BotMode.Club;
            if (index < 4)
            {
                var community = new OyinQCommunity { Key = key, Name = key, Mode = mode,
                    TelegramChatId = -1000 - index, TimeZoneId = "UTC", IsActive = true };
                if (mode == BotMode.Club) db.Clubs.Add(new Club { BotChat = community, BotChatKey = key, Name = key });
                else db.Camps.Add(new Camp { BotChat = community, BotChatKey = key, Name = key, Status = CampStatus.Closed });
                if (index < 3) db.ChatAdminPermissions.Add(new ChatAdminPermission { Community = community,
                    CommunityKey = key, TelegramUserId = 42, GrantedByTelegramUserId = 1 });
            }
            db.KnownTelegramChats.Add(new KnownTelegramChat { TelegramChatId = -1000 - index,
                IsBotPresent = true, TelegramPhotoFileId = key, TelegramPhotoUpdatedAt = DateTimeOffset.UtcNow });
            // Locked/unconfigured photos intentionally aren't cached. Any attempt
            // to retrieve them would hit the recording HTTP handler below.
            if (index < 3 || (superAdmin && index == 3))
                cache.Set($"telegram-community-photo:{key}", new byte[] { (byte)index });
        }
        await db.SaveChangesAsync();
        var verifier = new TelegramAdminVerifier(!superAdmin);
        var authorization = new AdminAuthorizationService(db, verifier, Options.Create(new AdministrationOptions
            { SuperAdminTelegramUserIds = superAdmin ? new HashSet<long> { 42 } : new HashSet<long>() }), TimeProvider.System);
        using var handler = new UnexpectedPhotoRequest();
        using var http = new HttpClient(handler);
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", http);
        var photos = new TelegramCommunityPhotoService(db, bot, cache, TimeProvider.System,
            NullLogger<TelegramCommunityPhotoService>.Instance);
        var authenticator = new TelegramMiniAppAuthenticator(Options.Create(new BotOptions { Token = "123:test" }), TimeProvider.System);
        var request = new DefaultHttpContext().Request;
        Assert.IsType<ForbidHttpResult>(await AdminEndpoints.OverviewAsync(request, db, authenticator, authorization, photos, default));
        request.Headers["X-Telegram-Init-Data"] = SignedData();

        var result = await AdminEndpoints.OverviewAsync(request, db, authenticator, authorization, photos, default);
        var json = JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var club = json.GetProperty("clubs")[0];
        Assert.Equal("data:image/jpeg;base64,AQ==", club.GetProperty("avatarUrl").GetString());
        Assert.Equal(superAdmin ? 2 : 1, json.GetProperty("clubs").GetArrayLength());
        var camp = Assert.Single(json.GetProperty("camps").EnumerateArray());
        Assert.Equal("Closed", camp.GetProperty("status").GetString());
        Assert.Equal("data:image/jpeg;base64,Ag==", camp.GetProperty("avatarUrl").GetString());
        Assert.All(json.GetProperty("lockedCommunities").EnumerateArray(), item => Assert.False(item.TryGetProperty("avatarUrl", out _)));
        Assert.Equal(0, handler.Requests);
        if (superAdmin) Assert.Equal(0, verifier.Checks);
    }

    private static string SignedData()
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        { ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ["user"] = "{\"id\":42}" };
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes("123:test"));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', values.Select(x => $"{x.Key}={x.Value}"))));
        return string.Join('&', values.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")) + "&hash=" + Convert.ToHexStringLower(hash);
    }

    private sealed class TelegramAdminVerifier(bool isAdmin) : ITelegramChatAdministratorVerifier
    {
        public int Checks { get; private set; }
        public Task<bool> IsAdministratorAsync(long chat, long user, CancellationToken ct)
        { Checks++; return Task.FromResult(isAdmin); }
        public Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(long chat, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class UnexpectedPhotoRequest : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)); }
    }
}
