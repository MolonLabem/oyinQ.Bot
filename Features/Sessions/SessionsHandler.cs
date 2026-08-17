using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Games;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Sessions;

public sealed class SessionsHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    GameSearchService gameSearchService,
    SessionMessageFormatter messageFormatter,
    IOptions<CampOptions> campOptions,
    ILogger<SessionsHandler> logger)
{
    private const int PageSize = 10;
    private const string SearchState = "sessions:search";

    public async Task<bool> TryHandleMessageAsync(
        Message message,
        long telegramUserId,
        ParticipantConversationState? conversationState,
        CancellationToken cancellationToken)
    {
        if (message.Text is not { } rawText)
        {
            return false;
        }

        if (conversationState?.State == SearchState)
        {
            if (message.Chat.Type != ChatType.Private)
            {
                return true;
            }

            await HandleSearchTextAsync(
                message.Chat.Id,
                telegramUserId,
                rawText.Trim(),
                cancellationToken);
            return true;
        }

        if (rawText.Trim() != "▶️ Собрать игру")
        {
            return false;
        }

        if (message.Chat.Type != ChatType.Private)
        {
            return true;
        }

        await ShowSourceMenuAsync(null, message.Chat.Id, cancellationToken);
        return true;
    }

    public async Task<bool> TryHandleCallbackAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrWhiteSpace(data)
            || !data.StartsWith("session:", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = data.Split(':');

        if (parts is ["session", "join", var joinSessionId]
            && long.TryParse(joinSessionId, out var sessionId))
        {
            await ChangeGroupParticipationAsync(
                callbackQuery,
                telegramUserId,
                sessionId,
                join: true,
                cancellationToken);
            return true;
        }

        if (parts is ["session", "leave", var leaveSessionId]
            && long.TryParse(leaveSessionId, out sessionId))
        {
            await ChangeGroupParticipationAsync(
                callbackQuery,
                telegramUserId,
                sessionId,
                join: false,
                cancellationToken);
            return true;
        }

        if (callbackQuery.Message?.Chat.Type != ChatType.Private)
        {
            return true;
        }

        var chatId = callbackQuery.Message.Chat.Id;

        if (data == "session:menu")
        {
            await ClearConversationStateAsync(telegramUserId, cancellationToken);
            await ShowSourceMenuAsync(callbackQuery, chatId, cancellationToken);
            return true;
        }

        if (parts is ["session", "list", var scope, var pageText]
            && int.TryParse(pageText, out var page))
        {
            await ShowGamePageAsync(
                callbackQuery,
                chatId,
                telegramUserId,
                scope,
                Math.Max(page, 0),
                cancellationToken);
            return true;
        }

        if (data == "session:search")
        {
            await StartSearchAsync(chatId, telegramUserId, cancellationToken);
            return true;
        }

        if (parts is ["session", "game", var gameIdText]
            && long.TryParse(gameIdText, out var gameId))
        {
            await ShowPlayerCountAsync(callbackQuery, chatId, gameId, cancellationToken);
            return true;
        }

        if (parts is ["session", "create", var createGameId, var wantedText]
            && long.TryParse(createGameId, out gameId)
            && int.TryParse(wantedText, out var wantedAdditionalPlayers)
            && wantedAdditionalPlayers is >= 1 and <= 4)
        {
            await CreateSessionAsync(
                callbackQuery,
                chatId,
                telegramUserId,
                gameId,
                wantedAdditionalPlayers,
                cancellationToken);
            return true;
        }

        if (parts is ["session", "close", var closeSessionId]
            && long.TryParse(closeSessionId, out sessionId))
        {
            await CloseSessionAsync(
                callbackQuery,
                telegramUserId,
                sessionId,
                cancelled: false,
                cancellationToken);
            return true;
        }

        if (parts is ["session", "cancel", var cancelSessionId]
            && long.TryParse(cancelSessionId, out sessionId))
        {
            await CloseSessionAsync(
                callbackQuery,
                telegramUserId,
                sessionId,
                cancelled: true,
                cancellationToken);
            return true;
        }

        return true;
    }

    private async Task ShowSourceMenuAsync(
        CallbackQuery? callbackQuery,
        long chatId,
        CancellationToken cancellationToken) =>
        await RenderPrivateAsync(
            callbackQuery,
            chatId,
            "▶️ Собрать игру\n\nВыберите игру:",
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("🔥 Популярные", "session:list:p:0")],
                [InlineKeyboardButton.WithCallbackData("🎒 Мои игры", "session:list:m:0")],
                [InlineKeyboardButton.WithCallbackData("🔎 Поиск", "session:search")]
            ]),
            cancellationToken);

    private async Task ShowGamePageAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long telegramUserId,
        string scope,
        int page,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Games
            .AsNoTracking()
            .Where(game => game.Copies.Any());

        if (scope == "m")
        {
            query = query.Where(game => game.Copies.Any(copy =>
                copy.Source == GameCopySource.Personal
                && copy.OwnerParticipant != null
                && copy.OwnerParticipant.TelegramUserId == telegramUserId));
        }

        var ordered = scope == "p"
            ? query.OrderByDescending(game => game.Interests.Count).ThenBy(game => game.Name)
            : query.OrderBy(game => game.Name);
        var pageResult = await ReadPageAsync(ordered, page, cancellationToken);
        var title = scope == "m" ? "🎒 Мои игры" : "🔥 Популярные игры";

        await RenderPrivateAsync(
            callbackQuery,
            chatId,
            BuildGameListText(title, pageResult.Items),
            BuildGameListKeyboard(pageResult, scope, page),
            cancellationToken);
    }

    private async Task ShowPlayerCountAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long gameId,
        CancellationToken cancellationToken)
    {
        var game = await dbContext.Games
            .AsNoTracking()
            .Where(value => value.Id == gameId && value.Copies.Any())
            .Select(value => new { value.Id, value.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (game is null)
        {
            await RenderPrivateAsync(
                callbackQuery,
                chatId,
                "Игра не найдена в каталоге.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ Назад", "session:menu")]
                ]),
                cancellationToken);
            return;
        }

        await RenderPrivateAsync(
            callbackQuery,
            chatId,
            $"🎲 {game.Name}\n\nСколько ещё игроков нужно?",
            new InlineKeyboardMarkup([
                [
                    InlineKeyboardButton.WithCallbackData("1", $"session:create:{game.Id}:1"),
                    InlineKeyboardButton.WithCallbackData("2", $"session:create:{game.Id}:2")
                ],
                [
                    InlineKeyboardButton.WithCallbackData("3", $"session:create:{game.Id}:3"),
                    InlineKeyboardButton.WithCallbackData("4+", $"session:create:{game.Id}:4")
                ],
                [InlineKeyboardButton.WithCallbackData("⬅️ Назад", "session:menu")]
            ]),
            cancellationToken);
    }

    private async Task StartSearchAsync(
        long chatId,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);
        var conversationState = await dbContext.ParticipantConversationStates.SingleOrDefaultAsync(
            value => value.ParticipantId == participant.Id,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (conversationState is null)
        {
            dbContext.ParticipantConversationStates.Add(new ParticipantConversationState
            {
                ParticipantId = participant.Id,
                State = SearchState,
                ExpiresAt = now.AddMinutes(30),
                UpdatedAt = now
            });
        }
        else
        {
            conversationState.State = SearchState;
            conversationState.DataJson = null;
            conversationState.ExpiresAt = now.AddMinutes(30);
            conversationState.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await botClient.SendMessage(
            chatId,
            "Введите часть названия игры из каталога.",
            cancellationToken: cancellationToken);
    }

    private async Task HandleSearchTextAsync(
        long chatId,
        long telegramUserId,
        string text,
        CancellationToken cancellationToken)
    {
        if (text.Length < 2)
        {
            await botClient.SendMessage(
                chatId,
                "Введите хотя бы 2 символа.",
                cancellationToken: cancellationToken);
            return;
        }

        await ClearConversationStateAsync(telegramUserId, cancellationToken);
        var ids = await gameSearchService.SearchCatalogIdsAsync(
            text,
            null,
            cancellationToken);

        if (ids.Count == 0)
        {
            await botClient.SendMessage(
                chatId,
                "Ничего не найдено в каталоге.",
                replyMarkup: new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ К выбору", "session:menu")]
                ]),
                cancellationToken: cancellationToken);
            return;
        }

        var games = await dbContext.Games
            .AsNoTracking()
            .Where(game => ids.Contains(game.Id))
            .Select(game => new GameListItem(game.Id, game.Name, game.Interests.Count))
            .ToArrayAsync(cancellationToken);
        var byId = games.ToDictionary(game => game.Id);
        var ordered = ids
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToArray();
        var rows = ordered
            .Select(game => (IEnumerable<InlineKeyboardButton>)[
                InlineKeyboardButton.WithCallbackData(
                    $"{game.Name} · 🔥 {game.InterestCount}",
                    $"session:game:{game.Id}")
            ])
            .ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData("⬅️ К выбору", "session:menu")]);

        await botClient.SendMessage(
            chatId,
            $"🔎 Результаты поиска: {text}",
            replyMarkup: new InlineKeyboardMarkup(rows),
            cancellationToken: cancellationToken);
    }

    private async Task CreateSessionAsync(
        CallbackQuery callbackQuery,
        long privateChatId,
        long telegramUserId,
        long gameId,
        int wantedAdditionalPlayers,
        CancellationToken cancellationToken)
    {
        var boardCampChatId = campOptions.Value.BoardCampChatId;
        if (boardCampChatId is null)
        {
            await RenderPrivateAsync(
                callbackQuery,
                privateChatId,
                "Чат BoardCamp не настроен. Укажите BOARD_CAMP_CHAT_ID.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ Назад", "session:menu")]
                ]),
                cancellationToken);
            return;
        }

        var participant = await dbContext.Participants.SingleAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);
        var game = await dbContext.Games
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == gameId && value.Copies.Any(),
                cancellationToken);

        if (game is null)
        {
            await RenderPrivateAsync(
                callbackQuery,
                privateChatId,
                "Игра не найдена в каталоге.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ Назад", "session:menu")]
                ]),
                cancellationToken);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var session = new GameSession
        {
            GameId = game.Id,
            HostParticipantId = participant.Id,
            WantedAdditionalPlayers = wantedAdditionalPlayers,
            Status = SessionStatus.Recruiting,
            CreatedAt = now,
            UpdatedAt = now,
            Participants =
            [
                new GameSessionParticipant
                {
                    ParticipantId = participant.Id,
                    JoinedAt = now
                }
            ]
        };

        dbContext.GameSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var loadedSession = await LoadSessionAsync(session.Id, tracking: false, cancellationToken)
                ?? throw new InvalidOperationException("Created session could not be reloaded.");
            var groupMessage = await botClient.SendMessage(
                boardCampChatId.Value,
                messageFormatter.Format(loadedSession),
                parseMode: ParseMode.Html,
                replyMarkup: BuildGroupKeyboard(loadedSession),
                cancellationToken: cancellationToken);

            session.TelegramChatId = groupMessage.Chat.Id;
            session.TelegramMessageId = groupMessage.Id;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Failed to publish game session {SessionId} to BoardCamp chat.", session.Id);
            dbContext.GameSessions.Remove(session);
            await dbContext.SaveChangesAsync(cancellationToken);

            await RenderPrivateAsync(
                callbackQuery,
                privateChatId,
                "Не удалось опубликовать сбор в чате BoardCamp. Проверьте BOARD_CAMP_CHAT_ID и права бота.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ Назад", "session:menu")]
                ]),
                cancellationToken);
            return;
        }

        await RenderPrivateAsync(
            callbackQuery,
            privateChatId,
            $"✅ Сбор создан: {game.Name}\nНужно ещё: {wantedAdditionalPlayers}\n\nСообщение опубликовано в чате BoardCamp.",
            BuildHostKeyboard(session.Id),
            cancellationToken);
    }

    private async Task ChangeGroupParticipationAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        long sessionId,
        bool join,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.Message is not { } callbackMessage)
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"GameSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE",
            cancellationToken);

        var session = await LoadSessionAsync(sessionId, tracking: true, cancellationToken);
        if (session is null
            || session.TelegramChatId != callbackMessage.Chat.Id
            || session.TelegramMessageId != callbackMessage.Id
            || session.Status == SessionStatus.Closed)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);
        if (participant is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var existing = session.Participants.SingleOrDefault(value =>
            value.ParticipantId == participant.Id);
        var totalPlayers = session.WantedAdditionalPlayers + 1;

        if (join)
        {
            if (participant.Id != session.HostParticipantId
                && existing is null
                && CurrentPlayerCount(session) < totalPlayers)
            {
                session.Participants.Add(new GameSessionParticipant
                {
                    ParticipantId = participant.Id,
                    JoinedAt = DateTimeOffset.UtcNow
                });
            }
        }
        else if (participant.Id != session.HostParticipantId && existing is not null)
        {
            var remainingPlayers = CurrentPlayerCount(session) - 1;
            session.Participants.Remove(existing);
            dbContext.GameSessionParticipants.Remove(existing);
            session.Status = remainingPlayers >= totalPlayers
                ? SessionStatus.Full
                : SessionStatus.Recruiting;
        }

        if (join)
        {
            session.Status = CurrentPlayerCount(session) >= totalPlayers
                ? SessionStatus.Full
                : SessionStatus.Recruiting;
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var updated = await LoadSessionAsync(sessionId, tracking: false, cancellationToken);
        if (updated is not null)
        {
            await TryRefreshGroupMessageAsync(updated, cancelled: false, cancellationToken);
        }
    }

    private async Task CloseSessionAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        long sessionId,
        bool cancelled,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.GameSessions
            .Include(value => value.HostParticipant)
            .Include(value => value.Game)
            .SingleOrDefaultAsync(value =>
                value.Id == sessionId
                && value.HostParticipant.TelegramUserId == telegramUserId,
                cancellationToken);

        if (session is null)
        {
            await RenderPrivateAsync(
                callbackQuery,
                telegramUserId,
                "Этот сбор вам недоступен.",
                EmptyKeyboard(),
                cancellationToken);
            return;
        }

        if (session.Status == SessionStatus.Closed)
        {
            await RenderPrivateAsync(
                callbackQuery,
                telegramUserId,
                "Этот сбор уже закрыт.",
                EmptyKeyboard(),
                cancellationToken);
            return;
        }

        session.Status = SessionStatus.Closed;
        session.ClosedAt = DateTimeOffset.UtcNow;
        session.UpdatedAt = session.ClosedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await LoadSessionAsync(session.Id, tracking: false, cancellationToken);
        var groupUpdated = updated is not null
            && await TryRefreshGroupMessageAsync(updated, cancelled, cancellationToken);
        var actionText = cancelled
            ? $"❌ Сбор отменён: {session.Game.Name}"
            : $"✅ Набор закрыт: {session.Game.Name}";
        if (!groupUpdated)
        {
            actionText += "\n\nНе удалось обновить сообщение в группе.";
        }

        await RenderPrivateAsync(
            callbackQuery,
            telegramUserId,
            actionText,
            EmptyKeyboard(),
            cancellationToken);
    }

    private async Task<bool> TryRefreshGroupMessageAsync(
        GameSession session,
        bool cancelled,
        CancellationToken cancellationToken)
    {
        if (session.TelegramChatId is not { } chatId
            || session.TelegramMessageId is not { } messageId)
        {
            return false;
        }

        try
        {
            await botClient.EditMessageText(
                chatId,
                messageId,
                messageFormatter.Format(session, cancelled),
                parseMode: ParseMode.Html,
                replyMarkup: session.Status == SessionStatus.Closed
                    ? EmptyKeyboard()
                    : BuildGroupKeyboard(session),
                cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Failed to refresh BoardCamp session message for session {SessionId}.",
                session.Id);
            return false;
        }
    }

    private Task<GameSession?> LoadSessionAsync(
        long sessionId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<GameSession> query = dbContext.GameSessions
            .Include(value => value.Game)
            .Include(value => value.HostParticipant)
            .Include(value => value.Participants)
                .ThenInclude(value => value.Participant);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(value => value.Id == sessionId, cancellationToken);
    }

    private async Task ClearConversationStateAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var conversationState = await dbContext.ParticipantConversationStates
            .SingleOrDefaultAsync(value =>
                value.Participant.TelegramUserId == telegramUserId,
                cancellationToken);
        if (conversationState is null)
        {
            return;
        }

        dbContext.ParticipantConversationStates.Remove(conversationState);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<PageResult> ReadPageAsync(
        IOrderedQueryable<Game> query,
        int page,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .Select(game => new GameListItem(game.Id, game.Name, game.Interests.Count))
            .Skip(page * PageSize)
            .Take(PageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > PageSize;
        return new PageResult(rows.Take(PageSize).ToArray(), hasMore);
    }

    private static string BuildGameListText(string title, IReadOnlyList<GameListItem> items)
    {
        if (items.Count == 0)
        {
            return $"{title}\n\nПока пусто.";
        }

        return string.Join(
            '\n',
            new[] { title, string.Empty }
                .Concat(items.Select((item, index) =>
                    $"{index + 1}. {item.Name} — 🔥 {item.InterestCount}")));
    }

    private static InlineKeyboardMarkup BuildGameListKeyboard(
        PageResult pageResult,
        string scope,
        int page)
    {
        var rows = pageResult.Items
            .Select(item => (IEnumerable<InlineKeyboardButton>)[
                InlineKeyboardButton.WithCallbackData(item.Name, $"session:game:{item.Id}")
            ])
            .ToList();
        var pagination = new List<InlineKeyboardButton>();

        if (page > 0)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData(
                "⬅️",
                $"session:list:{scope}:{page - 1}"));
        }

        if (pageResult.HasMore)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData(
                "Ещё ➡️",
                $"session:list:{scope}:{page + 1}"));
        }

        if (pagination.Count > 0)
        {
            rows.Add(pagination);
        }

        rows.Add([InlineKeyboardButton.WithCallbackData("⬅️ Назад", "session:menu")]);
        return new InlineKeyboardMarkup(rows);
    }

    private static InlineKeyboardMarkup BuildHostKeyboard(long sessionId) =>
        new([
            [InlineKeyboardButton.WithCallbackData("✅ Закрыть набор", $"session:close:{sessionId}")],
            [InlineKeyboardButton.WithCallbackData("❌ Отменить", $"session:cancel:{sessionId}")]
        ]);

    private static InlineKeyboardMarkup BuildGroupKeyboard(GameSession session) =>
        session.Status == SessionStatus.Full
            ? new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("➖ Выйти", $"session:leave:{session.Id}")]
            ])
            : new InlineKeyboardMarkup([
                [
                    InlineKeyboardButton.WithCallbackData("➕ Присоединиться", $"session:join:{session.Id}"),
                    InlineKeyboardButton.WithCallbackData("➖ Выйти", $"session:leave:{session.Id}")
                ]
            ]);

    private static InlineKeyboardMarkup EmptyKeyboard() =>
        new(Array.Empty<InlineKeyboardButton[]>());

    private async Task RenderPrivateAsync(
        CallbackQuery? callbackQuery,
        long chatId,
        string text,
        InlineKeyboardMarkup keyboard,
        CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message is { } message)
        {
            await botClient.EditMessageText(
                message.Chat.Id,
                message.Id,
                text,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(
            chatId,
            text,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private static int CurrentPlayerCount(GameSession session) =>
        1 + session.Participants.Count(value => value.ParticipantId != session.HostParticipantId);

    private sealed record GameListItem(long Id, string Name, int InterestCount);
    private sealed record PageResult(IReadOnlyList<GameListItem> Items, bool HasMore);
}
