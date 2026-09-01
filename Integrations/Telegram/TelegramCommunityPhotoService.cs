using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using oyinQ.Bot.Data;
using Telegram.Bot;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramCommunityPhotoService(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<TelegramCommunityPhotoService> logger)
{
    private static readonly TimeSpan MetadataLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ImageLifetime = TimeSpan.FromHours(6);

    public async Task<string?> GetDataUrlAsync(long telegramChatId, CancellationToken cancellationToken)
    {
        var known = await dbContext.KnownTelegramChats.SingleOrDefaultAsync(
            x => x.TelegramChatId == telegramChatId && x.IsBotPresent, cancellationToken);
        if (known is null) return null;
        var now = timeProvider.GetUtcNow();
        if (known.TelegramPhotoUpdatedAt is null || now - known.TelegramPhotoUpdatedAt >= MetadataLifetime)
        {
            try
            {
                var chat = await botClient.GetChat(telegramChatId, cancellationToken);
                known.Title = chat.Title;
                known.Username = chat.Username;
                known.IsForum = chat.IsForum;
                known.TelegramPhotoFileId = chat.Photo?.SmallFileId;
                known.TelegramPhotoUpdatedAt = now;
                known.UpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Could not refresh Telegram photo metadata for chat {ChatId}.",
                    telegramChatId);
            }
        }
        if (string.IsNullOrWhiteSpace(known.TelegramPhotoFileId)) return null;
        var fileId = known.TelegramPhotoFileId;
        if (cache.TryGetValue<byte[]>(PhotoCacheKey(fileId), out var cached) && cached is not null)
            return ToDataUrl(cached);
        try
        {
            var file = await botClient.GetFile(fileId, cancellationToken);
            await using var output = new MemoryStream();
            await botClient.DownloadFile(file, output, cancellationToken);
            var bytes = output.ToArray();
            if (bytes.Length == 0) return null;
            cache.Set(PhotoCacheKey(fileId), bytes, ImageLifetime);
            return ToDataUrl(bytes);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not download Telegram photo for known chat {ChatId}.",
                telegramChatId);
            known.TelegramPhotoFileId = null;
            known.TelegramPhotoUpdatedAt = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
    }

    private static string PhotoCacheKey(string fileId) => $"telegram-community-photo:{fileId}";
    private static string ToDataUrl(byte[] bytes) => $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
}
