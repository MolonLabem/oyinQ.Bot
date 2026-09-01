using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Features.Admin;

public sealed record PostingTopicOption(int MessageThreadId, string Name, bool IsClosed);

public sealed record PostingTopicSettings(
    bool IsForum,
    int? MessageThreadId,
    string? TopicName,
    bool NeedsSelection,
    IReadOnlyList<PostingTopicOption> KnownTopics);

public sealed class PostingTopicService(
    AppDbContext dbContext,
    IAdminAuthorizationService authorization,
    ITelegramChatForumCapabilityResolver forumCapabilityResolver,
    TimeProvider timeProvider)
{
    public async Task<PostingTopicSettings> GetAsync(long actorTelegramUserId, string communityKey,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync(actorTelegramUserId, communityKey, cancellationToken);
        var target = await TargetAsync(communityKey, cancellationToken);
        if (!await RefreshForumCapabilityAsync(target.TelegramChatId, cancellationToken))
            return new(false, null, null, false, []);

        var topics = await dbContext.TelegramForumTopics.AsNoTracking()
            .Where(x => x.TelegramChatId == target.TelegramChatId && !x.IsDeleted)
            .OrderBy(x => x.IsClosed).ThenBy(x => x.Name).ThenBy(x => x.MessageThreadId)
            .Select(x => new PostingTopicOption(x.MessageThreadId,
                x.Name ?? $"Тема #{x.MessageThreadId}", x.IsClosed))
            .ToArrayAsync(cancellationToken);
        var selected = topics.SingleOrDefault(x => x.MessageThreadId == target.PostingMessageThreadId);
        return new(true, selected?.MessageThreadId, selected?.Name,
            target.PostingTopicInvalidatedAt is not null
            || target.PostingMessageThreadId is not null && selected is null, topics);
    }

    public async Task SetAsync(long actorTelegramUserId, string communityKey, int? messageThreadId,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync(actorTelegramUserId, communityKey, cancellationToken);
        var target = await dbContext.OyinQCommunities.SingleAsync(x => x.Key == communityKey, cancellationToken);
        var isForum = await RefreshForumCapabilityAsync(target.TelegramChatId, cancellationToken);
        if (!isForum) throw new InvalidOperationException("В этом чате темы не используются.");
        if (messageThreadId is { } threadId)
        {
            var valid = await dbContext.TelegramForumTopics.AsNoTracking().AnyAsync(x =>
                x.TelegramChatId == target.TelegramChatId && x.MessageThreadId == threadId
                && !x.IsDeleted && !x.IsClosed, cancellationToken);
            if (!valid) throw new InvalidOperationException("Эта тема недоступна или не относится к выбранной группе.");
        }
        target.PostingMessageThreadId = messageThreadId;
        target.PostingTopicInvalidatedAt = null;
        target.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SelectFromTelegramAsync(long actorTelegramUserId, Message message,
        CancellationToken cancellationToken)
    {
        if (message.Chat.IsForum != true || message.MessageThreadId is not { } threadId)
            throw new InvalidOperationException("Отправьте команду из нужной темы форума.");
        var communityKey = await dbContext.OyinQCommunities.AsNoTracking()
            .Where(x => x.TelegramChatId == message.Chat.Id && x.DeletedAt == null)
            .Select(x => x.Key).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Эта группа не подключена к OyinQ.");
        await ObserveAsync(message, cancellationToken);
        await SetAsync(actorTelegramUserId, communityKey, threadId, cancellationToken);
    }

    public async Task ObserveAsync(Message message, CancellationToken cancellationToken)
    {
        if (message.Chat.IsForum != true) return;
        var threadId = message.MessageThreadId ?? (message.ForumTopicCreated is not null ? message.Id : (int?)null);
        if (threadId is null) return;
        var now = timeProvider.GetUtcNow();
        var topic = await dbContext.TelegramForumTopics.SingleOrDefaultAsync(x =>
            x.TelegramChatId == message.Chat.Id && x.MessageThreadId == threadId, cancellationToken);
        if (topic is null)
        {
            topic = new TelegramForumTopic
            {
                TelegramChatId = message.Chat.Id,
                MessageThreadId = threadId.Value
            };
            dbContext.TelegramForumTopics.Add(topic);
        }
        if (message.ForumTopicCreated is { } created) topic.Name = created.Name;
        if (!string.IsNullOrWhiteSpace(message.ForumTopicEdited?.Name)) topic.Name = message.ForumTopicEdited.Name;
        if (message.ForumTopicClosed is not null) topic.IsClosed = true;
        if (message.ForumTopicReopened is not null) topic.IsClosed = false;
        topic.IsDeleted = false;
        topic.LastSeenAt = now;
        if (message.ForumTopicClosed is not null)
        {
            var community = await dbContext.OyinQCommunities.SingleOrDefaultAsync(x =>
                x.TelegramChatId == message.Chat.Id && x.DeletedAt == null
                    && x.PostingMessageThreadId == threadId, cancellationToken);
            if (community is not null)
            {
                community.PostingMessageThreadId = null;
                community.PostingTopicInvalidatedAt = now;
                community.UpdatedAt = now;
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAuthorizedAsync(long actorTelegramUserId, string communityKey,
        CancellationToken cancellationToken)
    {
        if (!await authorization.CanAdministerCommunityAsync(actorTelegramUserId, communityKey, cancellationToken))
            throw new UnauthorizedAccessException("Нет доступа к настройкам этой группы.");
    }

    private async Task<bool> RefreshForumCapabilityAsync(long telegramChatId,
        CancellationToken cancellationToken)
    {
        var current = await forumCapabilityResolver.GetIsForumAsync(telegramChatId, cancellationToken);
        var known = await dbContext.KnownTelegramChats.SingleOrDefaultAsync(
            x => x.TelegramChatId == telegramChatId, cancellationToken);
        if (known is null) return current == true;
        if (current is null) return known.IsForum;
        if (known.IsForum == current.Value) return current.Value;
        known.IsForum = current.Value;
        known.UpdatedAt = timeProvider.GetUtcNow();
        if (!current.Value)
        {
            var community = await dbContext.OyinQCommunities.SingleOrDefaultAsync(
                x => x.TelegramChatId == telegramChatId && x.DeletedAt == null
                    && x.PostingMessageThreadId != null, cancellationToken);
            if (community is not null)
            {
                community.PostingMessageThreadId = null;
                community.PostingTopicInvalidatedAt = null;
                community.UpdatedAt = timeProvider.GetUtcNow();
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return current.Value;
    }

    private async Task<Target> TargetAsync(string communityKey, CancellationToken cancellationToken) =>
        await dbContext.OyinQCommunities.AsNoTracking().Where(x => x.Key == communityKey && x.DeletedAt == null)
            .Select(x => new Target(x.TelegramChatId, x.PostingMessageThreadId, x.PostingTopicInvalidatedAt))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("Сообщество не найдено.");

    private sealed record Target(long TelegramChatId, int? PostingMessageThreadId,
        DateTimeOffset? PostingTopicInvalidatedAt);
}
