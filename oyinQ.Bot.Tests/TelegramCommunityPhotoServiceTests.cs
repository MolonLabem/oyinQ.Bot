using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;

namespace oyinQ.Bot.Tests;

public sealed class TelegramCommunityPhotoServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KnownChatPhoto_UsesTelegramImageOrGracefulFallback(bool hasPhoto)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.KnownTelegramChats.Add(new KnownTelegramChat
        {
            TelegramChatId = -1001, IsBotPresent = true,
            FirstSeenAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO",
            new HttpClient(new PhotoHandler(hasPhoto)));
        var service = new TelegramCommunityPhotoService(db, bot, new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, NullLogger<TelegramCommunityPhotoService>.Instance);

        var result = await service.GetDataUrlAsync(-1001, default);

        if (hasPhoto)
        {
            Assert.StartsWith("data:image/jpeg;base64,", result);
            Assert.Equal("stable-photo-id", db.KnownTelegramChats.Single().TelegramPhotoFileId);
        }
        else
        {
            Assert.Null(result);
            Assert.Null(db.KnownTelegramChats.Single().TelegramPhotoFileId);
        }
    }

    [Fact]
    public async Task UnknownChatPhoto_IsNeverProxied()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var handler = new PhotoHandler(true);
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", new HttpClient(handler));
        var service = new TelegramCommunityPhotoService(db, bot, new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, NullLogger<TelegramCommunityPhotoService>.Instance);

        Assert.Null(await service.GetDataUrlAsync(-9999, default));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task StaleTelegramFile_FallsBackAndSchedulesMetadataRefresh()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.KnownTelegramChats.Add(new KnownTelegramChat
        {
            TelegramChatId = -1001, IsBotPresent = true, TelegramPhotoFileId = "stale",
            TelegramPhotoUpdatedAt = DateTimeOffset.UtcNow,
            FirstSeenAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var bot = new TelegramBotClient("123456:abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO",
            new HttpClient(new PhotoHandler(true, failDownload: true)));
        var service = new TelegramCommunityPhotoService(db, bot, new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, NullLogger<TelegramCommunityPhotoService>.Instance);

        Assert.Null(await service.GetDataUrlAsync(-1001, default));
        Assert.Null(db.KnownTelegramChats.Single().TelegramPhotoFileId);
        Assert.Null(db.KnownTelegramChats.Single().TelegramPhotoUpdatedAt);
    }

    private sealed class PhotoHandler(bool hasPhoto, bool failDownload = false) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("getChat", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(hasPhoto
                    ? "{\"ok\":true,\"result\":{\"id\":-1001,\"type\":\"supergroup\",\"title\":\"Club\",\"photo\":{\"small_file_id\":\"stable-photo-id\",\"small_file_unique_id\":\"small\",\"big_file_id\":\"big-photo-id\",\"big_file_unique_id\":\"big\"}}}"
                    : "{\"ok\":true,\"result\":{\"id\":-1001,\"type\":\"supergroup\",\"title\":\"Club\"}}"));
            if (path.Contains("getFile", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json("{\"ok\":true,\"result\":{\"file_id\":\"stable-photo-id\",\"file_unique_id\":\"small\",\"file_path\":\"photos/avatar.jpg\"}}"));
            return Task.FromResult(new HttpResponseMessage(failDownload ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            });
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
