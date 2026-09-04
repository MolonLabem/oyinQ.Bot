using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Notifications;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Integrations.Telegram;

public interface ITelegramGroupMessageSender
{
    Task<Func<CancellationToken, Task<Message>>> PrepareMessageAsync(string communityKey, string text, ParseMode parseMode,
        ReplyMarkup? replyMarkup, CancellationToken cancellationToken) =>
        Task.FromResult<Func<CancellationToken, Task<Message>>>(ct => SendMessageAsync(communityKey, text, parseMode, replyMarkup, ct));
    Task<Message> SendMessageAsync(string communityKey, string text, ParseMode parseMode,
        ReplyMarkup? replyMarkup, CancellationToken cancellationToken);
    Task<Message> SendPhotoAsync(string communityKey, InputFile photo, string caption, ParseMode parseMode,
        ReplyMarkup? replyMarkup, CancellationToken cancellationToken);
}

public sealed class TelegramGroupMessageSender(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    IOptions<AdministrationOptions> administrationOptions,
    NotificationService notifications,
    TimeProvider timeProvider,
    ILogger<TelegramGroupMessageSender> logger) : ITelegramGroupMessageSender
{
    public async Task<Func<CancellationToken, Task<Message>>> PrepareMessageAsync(string communityKey, string text,
        ParseMode parseMode, ReplyMarkup? replyMarkup, CancellationToken cancellationToken)
    {
        var destination = await ResolveAsync(communityKey, cancellationToken);
        return ct => SendResolvedAsync(communityKey, destination,
            (target, token) => botClient.SendMessage(target.ChatId, text, parseMode: parseMode, replyMarkup: replyMarkup,
                messageThreadId: target.MessageThreadId, cancellationToken: token), ct);
    }
    public Task<Message> SendMessageAsync(string communityKey, string text, ParseMode parseMode,
        ReplyMarkup? replyMarkup, CancellationToken cancellationToken) => SendWithFallbackAsync(
        communityKey,
        (destination, token) => botClient.SendMessage(destination.ChatId, text, parseMode: parseMode,
            replyMarkup: replyMarkup, messageThreadId: destination.MessageThreadId,
            cancellationToken: token),
        cancellationToken);

    public Task<Message> SendPhotoAsync(string communityKey, InputFile photo, string caption, ParseMode parseMode,
        ReplyMarkup? replyMarkup, CancellationToken cancellationToken) => SendWithFallbackAsync(
        communityKey,
        (destination, token) => botClient.SendPhoto(destination.ChatId, photo, caption: caption,
            parseMode: parseMode, replyMarkup: replyMarkup, messageThreadId: destination.MessageThreadId,
            cancellationToken: token),
        cancellationToken);

    private async Task<Message> SendWithFallbackAsync(string communityKey,
        Func<TelegramChatDestination, CancellationToken, Task<Message>> send,
        CancellationToken cancellationToken)
    {
        var destination = await ResolveAsync(communityKey, cancellationToken);
        return await SendResolvedAsync(communityKey, destination, send, cancellationToken);
    }

    private async Task<Message> SendResolvedAsync(string communityKey, TelegramChatDestination destination,
        Func<TelegramChatDestination, CancellationToken, Task<Message>> send, CancellationToken cancellationToken)
    {
        try
        {
            return await send(destination, cancellationToken);
        }
        catch (ApiRequestException exception) when (destination.MessageThreadId is { } threadId
                                                    && IsUnavailableThread(exception))
        {
            await InvalidateAsync(communityKey, destination.ChatId, threadId, exception, cancellationToken);
            return await send(destination with { MessageThreadId = null }, cancellationToken);
        }
    }

    private async Task<TelegramChatDestination> ResolveAsync(string communityKey,
        CancellationToken cancellationToken)
    {
        var community = await dbContext.OyinQCommunities.AsNoTracking()
            .Where(x => x.Key == communityKey)
            .Select(x => new { x.TelegramChatId, x.PostingMessageThreadId })
            .SingleAsync(cancellationToken);
        var isForum = await dbContext.KnownTelegramChats.AsNoTracking()
            .Where(x => x.TelegramChatId == community.TelegramChatId)
            .Select(x => x.IsForum).SingleOrDefaultAsync(cancellationToken);
        return new(community.TelegramChatId, isForum ? community.PostingMessageThreadId : null);
    }

    private async Task InvalidateAsync(string communityKey, long chatId, int threadId,
        ApiRequestException exception, CancellationToken cancellationToken)
    {
        logger.LogWarning(exception,
            "Configured Telegram topic {MessageThreadId} for {CommunityKey}/{TelegramChatId} is unavailable; retrying in the default chat.",
            threadId, communityKey, chatId);
        var community = await dbContext.OyinQCommunities.SingleAsync(x => x.Key == communityKey,
            cancellationToken);
        if (community.PostingMessageThreadId != threadId) return;
        community.PostingMessageThreadId = null;
        community.PostingTopicInvalidatedAt = timeProvider.GetUtcNow();
        community.UpdatedAt = timeProvider.GetUtcNow();
        var topic = await dbContext.TelegramForumTopics.SingleOrDefaultAsync(x =>
            x.TelegramChatId == chatId && x.MessageThreadId == threadId, cancellationToken);
        if (topic is not null)
        {
            if (IsClosedThread(exception)) topic.IsClosed = true;
            else topic.IsDeleted = true;
            topic.LastSeenAt = timeProvider.GetUtcNow();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAdministratorsAsync(community, cancellationToken);
    }

    private async Task NotifyAdministratorsAsync(Data.Entities.OyinQCommunity community,
        CancellationToken cancellationToken)
    {
        var approved = await dbContext.ChatAdminPermissions.AsNoTracking()
            .Where(x => x.CommunityKey == community.Key && x.RevokedAt == null)
            .Select(x => x.TelegramUserId).ToArrayAsync(cancellationToken);
        var recipients = approved.Concat(administrationOptions.Value.SuperAdminTelegramUserIds).Distinct();
        foreach (var telegramUserId in recipients)
        {
            try
            {
                await notifications.EnqueueAsync(new(telegramUserId, NotificationKind.PostingTopicUnavailable,
                    $"{community.Key}:{community.PostingTopicInvalidatedAt?.UtcTicks}",
                    $"Тема для публикаций в группе «{community.Name}» больше недоступна. Выберите её заново в админ-панели.",
                    community.Key), cancellationToken);
            }
            catch (Exception notifyException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(notifyException,
                    "Could not notify Telegram administrator {TelegramUserId} about invalid topic for {CommunityKey}.",
                    telegramUserId, community.Key);
            }
        }
    }

    public static bool IsUnavailableThread(ApiRequestException exception)
    {
        if (exception.ErrorCode != 400) return false;
        var message = exception.Message.ToLowerInvariant();
        return message.Contains("message thread not found", StringComparison.Ordinal)
               || message.Contains("topic was deleted", StringComparison.Ordinal)
               || message.Contains("topic_closed", StringComparison.Ordinal)
               || message.Contains("topic is closed", StringComparison.Ordinal)
               || message.Contains("message thread is closed", StringComparison.Ordinal);
    }

    private static bool IsClosedThread(ApiRequestException exception) =>
        exception.Message.Contains("closed", StringComparison.OrdinalIgnoreCase);

    private sealed record TelegramChatDestination(long ChatId, int? MessageThreadId);
}
