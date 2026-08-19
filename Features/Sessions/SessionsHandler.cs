using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Games;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
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

        var text = rawText.Trim();
        if (text is not ("▶️ Собрать игру" or "🎲 Текущие сборы"))
        {
            return false;
        }

        if (message.Chat.Type != ChatType.Private)
        {
            return true;
        }

        if (text == "🎲 Текущие сборы")
        {
            await ShowActiveSessionsAsync(null, message.Chat.Id, 0, cancellationToken);
        }
        else
        {
            await ShowSourceMenuAsync(null, message.Chat.Id, cancellationToken);
        }

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
        var isGroupJoinLeave = parts is ["session", "join" or "leave", _];
        if (callbackQuery.Message?.Chat.Type != ChatType.Private && !isGroupJoinLeave)
        {
            await AnswerAlertAsync(
                callbackQuery,
                "Меню и управление сборами доступны в личном чате с ботом.",
                cancellationToken);
            return true;
        }

        if (parts is ["session", "join", var joinSessionId]
            && long.TryParse(joinSessionId, out var sessionId))
        {
            await ChangeParticipationAsync(
                callbackQuery,
                telegramUserId,
                sessionId,
                join: true,
                requireGroupMessageIdentity: true,
                refreshPrivateView: false,
                cancellationToken);
            return true;
        }

        if (parts is ["session", "leave", var leaveSessionId]
            && long.TryParse(leaveSessionId, out sessionId))
        {
            await ChangeParticipationAsync(
                callbackQuery,
                telegramUserId,
                sessionId,
                join: false,
                requireGroupMessageIdentity: true,
                refreshPrivateView: false,
                cancellationToken);
            return true;
        }

        if (parts is ["session", "pjoin", var privateJoinSessionId]
            && long.TryParse(privateJoinSessionId, out sessionId))
        {
            await ChangeParticipationAsync(
                callbackQuery,
                telegramUserId,
                sessionId,
                join: true,
                requireGroupMessageIdentity: false,
                refreshPrivateView: true,
                cancellationToken);
            return true;
        }

        if (parts is ["session", "pleave", var privateLeaveSessionId]
            && long.TryParse(privateLeaveSessionId, out sessionId))
        {
            await ChangeParticipationAsync(
                callbackQuery,
                telegramUserId,
                sessionId,
                join: false,
                requireGroupMessageIdentity: false,
                refreshPrivateView: true,
                cancellationToken);
            return true;
        }

        var chatId = callbackQuery.Message?.Chat.Id ?? telegramUserId;

        if (data == "session:menu")
        {
            await AcknowledgeAsync(callbackQuery, cancellationToken);
            await ClearConversationStateAsync(telegramUserId, cancellationToken);
            await ShowSourceMenuAsync(callbackQuery, chatId, cancellationToken);
            return true;
        }

        if (parts is ["session", "active", var activePageText]
            && int.TryParse(activePageText, out var activePage))
        {
            await AcknowledgeAsync(callbackQuery, cancellationToken);
            await ShowActiveSessionsAsync(
                callbackQuery,
                chatId,
                Math.Max(activePage, 0),
                cancellationToken);
            return true;
        }

        if (parts is ["session", "view", var viewSessionId]
            && long.TryParse(viewSessionId, out sessionId))
        {
            await ShowActiveSessionAsync(
                callbackQuery,
                chatId,
                telegramUserId,
                sessionId,
                answerCallback: true,
                cancellationToken);
            return true;
        }

        if (parts is ["session", "list", var scope, var pageText]
            && int.TryParse(pageText, out var page))
        {
            await AcknowledgeAsync(callbackQuery, cancellationToken);
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
            await AcknowledgeAsync(callbackQuery, cancellationToken);
            await StartSearchAsync(chatId, telegramUserId, cancellationToken);
            return true;
        }

        if (parts is ["session", "game", var gameIdText]
            && long.TryParse(gameIdText, out var gameId))
        {
            await AcknowledgeAsync(callbackQuery, cancellationToken);
            await ShowPlayerCountAsync(callbackQuery, chatId, gameId, cancellationToken);
            return true;
        }

        if (parts is ["session", "create", var createGameId, var wantedText]
            && long.TryParse(createGameId, out gameId)
            && int.TryParse(wantedText, out var wantedAdditionalPlayers)
            && wantedAdditionalPlayers is >= 1 and <= 4)
        {
            await AcknowledgeAsync(callbackQuery, cancellationToken);
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

        await AnswerAlertAsync(callbackQuery, "Эта кнопка устарела.", cancellationToken);
        return true;
    }

    private async Task ShowSourceMenuAsync(
        CallbackQuery? callbackQuery,
        long chatId,
        CancellationToken cancellationToken) =>
        await RenderPrivateAsync(
            callbackQuery,
            chatId,
            """
            ▶️ Собрать игру

            Создайте новый набор игроков. Выберите игру из каталога, своих игр или через поиск, затем укажите, сколько дополнительных игроков нужно.

            Сам набор будет опубликован одним сообщением в группе BoardCamp; дальнейшие изменения состава обновляют это сообщение на месте.
            """,
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("🔥 По спросу", "session:list:p:0")],
                [InlineKeyboardButton.WithCallbackData("🎒 Мои игры", "session:list:m:0")],
                [InlineKeyboardButton.WithCallbackData("🔎 Поиск", "session:search")],
                [InlineKeyboardButton.WithCallbackData("🎲 Текущие сборы", "session:active:0")]
            ]),
            cancellationToken);

    private async Task ShowActiveSessionsAsync(
        CallbackQuery? callbackQuery,
        long chatId,
        int page,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.GameSessions
            .AsNoTracking()
            .Where(value => value.Status == SessionStatus.Recruiting || value.Status == SessionStatus.Full)
            .OrderByDescending(value => value.CreatedAt)
            .Select(value => new ActiveSessionItem(
                value.Id,
                value.Game.Name,
                value.Status,
                value.Participants.Count,
                value.WantedAdditionalPlayers + 1))
            .Skip(page * PageSize)
            .Take(PageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > PageSize;
        var items = rows.Take(PageSize).ToArray();

        var text = items.Length == 0
            ? "🎲 Текущие сборы\n\nСейчас открытых сборов нет. Здесь появятся наборы, опубликованные в группе BoardCamp."
            : "🎲 Текущие сборы\n\nОткрытые наборы игроков. «Состав набран» остаётся в списке, пока организатор не закроет сбор; если кто-то выйдет, набор снова откроется.";

        var keyboardRows = items
            .Select(item => (IEnumerable<InlineKeyboardButton>)[
                InlineKeyboardButton.WithCallbackData(
                    $"{FormatActiveStatus(item.Status)} {item.GameName} · {item.PlayerCount}/{item.TotalPlayers}",
                    $"session:view:{item.Id}")
            ])
            .ToList();
        var pagination = new List<InlineKeyboardButton>();
        if (page > 0)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData("⬅️", $"session:active:{page - 1}"));
        }
        if (hasMore)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData("Ещё ➡️", $"session:active:{page + 1}"));
        }
        if (pagination.Count > 0)
        {
            keyboardRows.Add(pagination);
        }
        keyboardRows.Add([InlineKeyboardButton.WithCallbackData("▶️ Создать сбор", "session:menu")]);

        await RenderPrivateAsync(
            callbackQuery,
            chatId,
            text,
            new InlineKeyboardMarkup(keyboardRows),
            cancellationToken);
    }

    private async Task ShowActiveSessionAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long telegramUserId,
        long sessionId,
        bool answerCallback,
        CancellationToken cancellationToken)
    {
        var session = await LoadSessionAsync(sessionId, tracking: false, cancellationToken);
        if (session is null || session.Status == SessionStatus.Closed)
        {
            if (answerCallback)
            {
                await AnswerAlertAsync(callbackQuery, "Этот сбор уже закрыт или больше не существует.", cancellationToken);
            }

            await ShowActiveSessionsAsync(callbackQuery, chatId, 0, cancellationToken);
            return;
        }

        if (answerCallback)
        {
            await AcknowledgeAsync(callbackQuery, cancellationToken);
        }

        var participant = await dbContext.Participants
            .AsNoTracking()
            .SingleAsync(value => value.TelegramUserId == telegramUserId, cancellationToken);
        var isHost = participant.Id == session.HostParticipantId;
        var isParticipant = session.Participants.Any(value => value.ParticipantId == participant.Id);
        var rows = new List<IEnumerable<InlineKeyboardButton>>();

        if (isHost)
        {
            rows.Add([InlineKeyboardButton.WithCallbackData("✅ Закрыть набор", $"session:close:{session.Id}")]);
            rows.Add([InlineKeyboardButton.WithCallbackData("❌ Отменить", $"session:cancel:{session.Id}")]);
        }
        else if (isParticipant)
        {
            rows.Add([InlineKeyboardButton.WithCallbackData("➖ Выйти", $"session:pleave:{session.Id}")]);
        }
        else if (session.Status == SessionStatus.Recruiting)
        {
            rows.Add([InlineKeyboardButton.WithCallbackData("➕ Присоединиться", $"session:pjoin:{session.Id}")]);
        }

        rows.Add([InlineKeyboardButton.WithCallbackData("⬅️ К текущим сборам", "session:active:0")]);
        await RenderPrivateAsync(
            callbackQuery,
            chatId,
            messageFormatter.Format(session),
            new InlineKeyboardMarkup(rows),
            cancellationToken,
            ParseMode.Html);
    }

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
        var title = scope == "m"
            ? "🎒 Мои игры — игры, которые есть в вашей личной коллекции"
            : "🔥 По спросу — игры, которые участники чаще всего отметили «Хочу сыграть»";

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
            $"🎲 {game.Name}\n\nСколько ещё игроков нужно помимо вас?",
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
            "Введите часть названия игры из уже добавленного каталога.",
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
                "Чат BoardCamp не настроен. Обратитесь к администратору.",
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
                "Не удалось опубликовать сбор в чате BoardCamp. Проверьте настройки чата и права бота.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ Назад", "session:menu")]
                ]),
                cancellationToken);
            return;
        }

        await RenderPrivateAsync(
            callbackQuery,
            privateChatId,
            $"✅ Сбор создан: {game.Name}\nНужно ещё: {wantedAdditionalPlayers}\n\nВ группе BoardCamp опубликовано одно сообщение набора; состав будет обновляться в нём.",
            BuildHostKeyboard(session.Id),
            cancellationToken);
    }

    private async Task ChangeParticipationAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        long sessionId,
        bool join,
        bool requireGroupMessageIdentity,
        bool refreshPrivateView,
        CancellationToken cancellationToken)
    {
        var callbackMessage = callbackQuery.Message;
        if (callbackMessage is null)
        {
            await AnswerAlertAsync(callbackQuery, "Не удалось определить сообщение сбора.", cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"GameSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE",
            cancellationToken);

        var session = await LoadSessionAsync(sessionId, tracking: true, cancellationToken);
        if (session is null)
        {
            await transaction.CommitAsync(cancellationToken);
            await AnswerAlertAsync(callbackQuery, "Этот сбор больше не существует.", cancellationToken);
            return;
        }

        if (requireGroupMessageIdentity
            && (session.TelegramChatId != callbackMessage.Chat.Id
                || session.TelegramMessageId != callbackMessage.Id))
        {
            await transaction.CommitAsync(cancellationToken);
            await AnswerAlertAsync(callbackQuery, "Эта кнопка относится к устаревшему сообщению сбора.", cancellationToken);
            return;
        }

        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);
        if (participant is null)
        {
            await transaction.CommitAsync(cancellationToken);
            await AnswerAlertAsync(
                callbackQuery,
                "Сначала откройте бота в личном чате и зарегистрируйтесь.",
                cancellationToken);
            return;
        }

        var existingBeforeChange = session.Participants.SingleOrDefault(value =>
            value.ParticipantId == participant.Id);
        var change = SessionParticipationLogic.Apply(
            session,
            participant,
            join,
            DateTimeOffset.UtcNow);

        if (change.Changed)
        {
            if (!join && existingBeforeChange is not null)
            {
                dbContext.GameSessionParticipants.Remove(existingBeforeChange);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await AnswerParticipationResultAsync(callbackQuery, change.Result, cancellationToken);

        if (!change.Changed)
        {
            return;
        }

        var updated = await LoadSessionAsync(sessionId, tracking: false, cancellationToken);
        if (updated is null)
        {
            return;
        }

        await TryRefreshGroupMessageAsync(updated, cancelled: false, cancellationToken);
        if (refreshPrivateView && callbackMessage.Chat.Type == ChatType.Private)
        {
            await ShowActiveSessionAsync(
                callbackQuery,
                callbackMessage.Chat.Id,
                telegramUserId,
                sessionId,
                answerCallback: false,
                cancellationToken);
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
            await AnswerAlertAsync(callbackQuery, "Этот сбор вам недоступен.", cancellationToken);
            return;
        }

        if (session.Status == SessionStatus.Closed)
        {
            await AnswerAlertAsync(callbackQuery, "Этот сбор уже закрыт.", cancellationToken);
            return;
        }

        session.Status = SessionStatus.Closed;
        session.ClosedAt = DateTimeOffset.UtcNow;
        session.UpdatedAt = session.ClosedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        await botClient.AnswerCallbackQuery(
            callbackQuery.Id,
            cancelled ? "Сбор отменён." : "Набор закрыт.",
            cancellationToken: cancellationToken);

        var updated = await LoadSessionAsync(session.Id, tracking: false, cancellationToken);
        var groupUpdated = updated is not null
            && await TryRefreshGroupMessageAsync(updated, cancelled, cancellationToken);
        var actionText = cancelled
            ? $"❌ Сбор отменён: {session.Game.Name}"
            : $"✅ Набор закрыт: {session.Game.Name}";
        if (!groupUpdated)
        {
            actionText += "\n\nНе удалось обновить исходное сообщение в группе.";
        }

        await RenderPrivateAsync(
            callbackQuery,
            telegramUserId,
            actionText,
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("🎲 Текущие сборы", "session:active:0")]
            ]),
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
        catch (ApiRequestException exception) when (
            exception.ErrorCode == 400
            && exception.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "BoardCamp session message {SessionId} already shows the desired state.",
                session.Id);
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
            [InlineKeyboardButton.WithCallbackData("❌ Отменить", $"session:cancel:{sessionId}")],
            [InlineKeyboardButton.WithCallbackData("🎲 Текущие сборы", "session:active:0")]
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
        CancellationToken cancellationToken,
        ParseMode? parseMode = null)
    {
        if (callbackQuery?.Message is { } message)
        {
            await botClient.EditMessageText(
                message.Chat.Id,
                message.Id,
                text,
                parseMode: parseMode,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(
            chatId,
            text,
            parseMode: parseMode,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private Task AcknowledgeAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken) =>
        botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

    private Task AnswerAlertAsync(
        CallbackQuery callbackQuery,
        string text,
        CancellationToken cancellationToken) =>
        botClient.AnswerCallbackQuery(
            callbackQuery.Id,
            text,
            showAlert: true,
            cancellationToken: cancellationToken);

    private async Task AnswerParticipationResultAsync(
        CallbackQuery callbackQuery,
        SessionParticipationResult result,
        CancellationToken cancellationToken)
    {
        var (text, showAlert) = result switch
        {
            SessionParticipationResult.Joined => ("Вы присоединились.", false),
            SessionParticipationResult.Left => ("Вы вышли из сбора.", false),
            SessionParticipationResult.AlreadyJoined => ("Вы уже участвуете в этом сборе.", true),
            SessionParticipationResult.NotJoined => ("Вы не участвуете в этом сборе.", true),
            SessionParticipationResult.HostAlreadyJoined => ("Вы организатор и уже входите в состав.", true),
            SessionParticipationResult.HostCannotLeave => ("Организатор не может выйти. Закройте или отмените сбор в личном чате.", true),
            SessionParticipationResult.Full => ("Состав уже набран. Если кто-то выйдет, набор снова откроется.", true),
            SessionParticipationResult.Closed => ("Этот сбор уже закрыт.", true),
            _ => ("Действие недоступно.", true)
        };

        await botClient.AnswerCallbackQuery(
            callbackQuery.Id,
            text,
            showAlert: showAlert,
            cancellationToken: cancellationToken);
    }

    private static string FormatActiveStatus(SessionStatus status) =>
        status == SessionStatus.Full ? "✅" : "🟢";

    private sealed record GameListItem(long Id, string Name, int InterestCount);
    private sealed record PageResult(IReadOnlyList<GameListItem> Items, bool HasMore);
    private sealed record ActiveSessionItem(
        long Id,
        string GameName,
        SessionStatus Status,
        int PlayerCount,
        int TotalPlayers);
}
