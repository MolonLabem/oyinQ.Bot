using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Games;

public sealed partial class GamesHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    GameSearchService searchService,
    GameDedupService dedupService,
    ILogger<GamesHandler> logger)
{
    private const int PageSize = 10;
    private const string CatalogSearchState = "games:catalog-search";
    private const string AddSearchState = "games:add-search";
    private const string MyGamesSearchState = "games:my-search";

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

        var text = rawText.Trim();
        if (conversationState?.State is CatalogSearchState or AddSearchState or MyGamesSearchState)
        {
            await HandleConversationTextAsync(
                message.Chat.Id,
                telegramUserId,
                conversationState.State,
                text,
                cancellationToken);
            return true;
        }

        switch (text)
        {
            case "🎲 Игры":
            case "/games":
                await ShowCatalogMenuAsync(message.Chat.Id, cancellationToken);
                return true;

            case "➕ Добавить игры":
            case "/addgame":
                await StartSearchAsync(
                    message.Chat.Id,
                    telegramUserId,
                    AddSearchState,
                    "Отправьте название игры или ссылку BGG вида boardgamegeek.com/boardgame/12345.",
                    cancellationToken);
                return true;

            case "🔥 Хочу сыграть":
            case "/wanted":
                await ShowWantedAsync(null, message.Chat.Id, 0, cancellationToken);
                return true;

            case "Мои игры":
            case "🎲 Мои игры":
            case "/mygames":
                await ShowMyGamesMenuAsync(null, message.Chat.Id, cancellationToken);
                return true;

            case "Мои хотелки":
            case "🔥 Мои хотелки":
                await ShowMyWantedAsync(
                    null,
                    message.Chat.Id,
                    telegramUserId,
                    0,
                    cancellationToken);
                return true;

            default:
                return false;
        }
    }

    public async Task<bool> TryHandleCallbackAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        var chatId = callbackQuery.Message?.Chat.Id ?? telegramUserId;
        var parts = data.Split(':');

        if (data == "game:menu")
        {
            await ShowCatalogMenuAsync(callbackQuery, chatId, cancellationToken);
            return true;
        }

        if (data == "game:my:menu")
        {
            await ShowMyGamesMenuAsync(callbackQuery, chatId, cancellationToken);
            return true;
        }

        if (parts is ["game", "list", var filter, var pageText]
            && int.TryParse(pageText, out var page))
        {
            await ShowCatalogPageAsync(
                callbackQuery,
                chatId,
                filter,
                Math.Max(page, 0),
                cancellationToken);
            return true;
        }

        if (parts is ["game", "wanted", var wantedPage]
            && int.TryParse(wantedPage, out page))
        {
            await ShowWantedAsync(
                callbackQuery,
                chatId,
                Math.Max(page, 0),
                cancellationToken);
            return true;
        }

        if (parts is ["game", "mywanted", var myWantedPage]
            && int.TryParse(myWantedPage, out page))
        {
            await ShowMyWantedAsync(
                callbackQuery,
                chatId,
                telegramUserId,
                Math.Max(page, 0),
                cancellationToken);
            return true;
        }

        if (parts is ["game", "my", var myFilter, var myPage]
            && int.TryParse(myPage, out page))
        {
            await ShowMyGamesPageAsync(
                callbackQuery,
                chatId,
                telegramUserId,
                myFilter,
                Math.Max(page, 0),
                cancellationToken);
            return true;
        }

        if (parts is ["game", "search", var searchScope])
        {
            var state = searchScope == "my" ? MyGamesSearchState : CatalogSearchState;
            var prompt = searchScope == "my"
                ? "Введите часть названия среди ваших игр."
                : "Введите часть названия игры в каталоге.";

            await StartSearchAsync(
                chatId,
                telegramUserId,
                state,
                prompt,
                cancellationToken);
            return true;
        }

        if (parts is ["game", "card", var gameIdText, var context, var cardPage]
            && long.TryParse(gameIdText, out var gameId)
            && int.TryParse(cardPage, out page))
        {
            await ShowGameCardAsync(
                callbackQuery,
                telegramUserId,
                gameId,
                context,
                Math.Max(page, 0),
                cancellationToken);
            return true;
        }

        if (parts is ["game", "add", var bggIdText]
            && long.TryParse(bggIdText, out var bggId))
        {
            await AddBggCandidateAsync(
                callbackQuery,
                chatId,
                bggId,
                cancellationToken);
            return true;
        }

        if (parts is ["copy", "add", var addGameId, var addStatus]
            && long.TryParse(addGameId, out gameId)
            && TryParseBringStatus(addStatus, out var bringStatus))
        {
            await dedupService.AddOrUpdatePersonalCopyAsync(
                gameId,
                telegramUserId,
                bringStatus,
                cancellationToken);
            await ShowGameCardAsync(
                callbackQuery,
                telegramUserId,
                gameId,
                "mg",
                0,
                cancellationToken);
            return true;
        }

        if (parts is ["copy", "confirm", var copyIdText, var targetStatus]
            && long.TryParse(copyIdText, out var copyId)
            && TryParseBringStatus(targetStatus, out bringStatus))
        {
            await ShowStatusConfirmationAsync(
                callbackQuery,
                telegramUserId,
                copyId,
                bringStatus,
                cancellationToken);
            return true;
        }

        if (parts is ["copy", "set", var setCopyIdText, var setStatus]
            && long.TryParse(setCopyIdText, out copyId)
            && TryParseBringStatus(setStatus, out bringStatus))
        {
            await ChangeOwnedCopyStatusAsync(
                callbackQuery,
                telegramUserId,
                copyId,
                bringStatus,
                cancellationToken);
            return true;
        }

        return false;
    }

    public async Task ShowGameCardAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        long gameId,
        string context,
        int page,
        CancellationToken cancellationToken)
    {
        var participantId = await dbContext.Participants
            .Where(value => value.TelegramUserId == telegramUserId)
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);

        var game = await dbContext.Games
            .AsNoTracking()
            .Include(value => value.Copies)
                .ThenInclude(value => value.OwnerParticipant)
            .Include(value => value.Interests)
            .SingleOrDefaultAsync(value => value.Id == gameId, cancellationToken);

        if (game is null)
        {
            await RenderAsync(
                callbackQuery,
                telegramUserId,
                "Игра не найдена.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ К каталогу", "game:menu")]
                ]),
                cancellationToken);
            return;
        }

        var text = new StringBuilder();
        text.AppendLine($"🎲 {game.Name}");
        text.AppendLine($"Игроки: {FormatPlayers(game.MinPlayers, game.MaxPlayers)}");
        text.AppendLine($"Лучше всего: {game.BestPlayers ?? "—"}");
        text.AppendLine($"🔥 Хотят сыграть: {game.Interests.Count}");
        text.AppendLine();
        text.AppendLine("Копии:");

        if (game.Copies.Count == 0)
        {
            text.AppendLine("— пока нет");
        }
        else
        {
            foreach (var copy in game.Copies
                         .OrderBy(value => value.Source)
                         .ThenBy(value => value.OwnerParticipant?.DisplayName))
            {
                if (copy.Source == GameCopySource.Club)
                {
                    text.AppendLine("🏢 Клуб — точно будет");
                    continue;
                }

                var owner = copy.OwnerParticipant?.DisplayName ?? "Участник";
                var status = copy.BringStatus == BringStatus.Bringing ? "✅ возьмёт" : "🤔 возможно";
                text.AppendLine($"{status} — {owner}");
            }
        }

        var rows = new List<IEnumerable<InlineKeyboardButton>>();
        var isInterested = game.Interests.Any(value => value.ParticipantId == participantId);
        rows.Add([
            InlineKeyboardButton.WithCallbackData(
                isInterested ? "🔥 Убрать из хотелок" : "🔥 Хочу сыграть",
                $"interest:toggle:{game.Id}")
        ]);

        var ownCopy = game.Copies.SingleOrDefault(value =>
            value.Source == GameCopySource.Personal
            && value.OwnerParticipant?.TelegramUserId == telegramUserId);
        if (ownCopy is not null)
        {
            var target = ownCopy.BringStatus == BringStatus.Maybe ? "b" : "m";
            var label = ownCopy.BringStatus == BringStatus.Maybe
                ? "✅ Я точно возьму"
                : "🤔 Поставить «Возможно»";
            rows.Add([
                InlineKeyboardButton.WithCallbackData(
                    label,
                    $"copy:confirm:{ownCopy.Id}:{target}")
            ]);
        }

        if (!string.IsNullOrWhiteSpace(game.ExternalUrl))
        {
            rows.Add([InlineKeyboardButton.WithUrl("🔗 Открыть BGG", game.ExternalUrl)]);
        }

        rows.Add([
            InlineKeyboardButton.WithCallbackData(
                "⬅️ Назад",
                BuildBackCallback(context, page))
        ]);

        await RenderAsync(
            callbackQuery,
            telegramUserId,
            text.ToString().TrimEnd(),
            new InlineKeyboardMarkup(rows),
            cancellationToken);
    }

    private async Task ShowCatalogMenuAsync(long chatId, CancellationToken cancellationToken) =>
        await botClient.SendMessage(
            chatId,
            "🎲 Каталог игр",
            replyMarkup: CatalogMenuKeyboard(),
            cancellationToken: cancellationToken);

    private async Task ShowCatalogMenuAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken) =>
        await RenderAsync(
            callbackQuery,
            chatId,
            "🎲 Каталог игр",
            CatalogMenuKeyboard(),
            cancellationToken);

    private static InlineKeyboardMarkup CatalogMenuKeyboard() =>
        new([
            [InlineKeyboardButton.WithCallbackData("🔥 Популярные", "game:list:p:0")],
            [InlineKeyboardButton.WithCallbackData("✅ Точно будут", "game:list:b:0")],
            [InlineKeyboardButton.WithCallbackData("🤔 Возможно", "game:list:m:0")],
            [InlineKeyboardButton.WithCallbackData("🔎 Поиск", "game:search:catalog")],
            [InlineKeyboardButton.WithCallbackData("🎒 Мои игры", "game:my:menu")]
        ]);

    private async Task ShowCatalogPageAsync(
        CallbackQuery callbackQuery,
        long chatId,
        string filter,
        int page,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Games
            .AsNoTracking()
            .Where(game => game.Copies.Any());

        query = filter switch
        {
            "b" => query.Where(game => game.Copies.Any(copy =>
                copy.Source == GameCopySource.Club
                || copy.BringStatus == BringStatus.Bringing)),
            "m" => query.Where(game => game.Copies.Any(copy =>
                copy.Source == GameCopySource.Personal
                && copy.BringStatus == BringStatus.Maybe)),
            _ => query
        };

        var ordered = filter == "p"
            ? query.OrderByDescending(game => game.Interests.Count).ThenBy(game => game.Name)
            : query.OrderBy(game => game.Name);

        var pageResult = await ReadPageAsync(ordered, page, cancellationToken);
        var title = filter switch
        {
            "b" => "✅ Точно будут",
            "m" => "🤔 Возможно",
            _ => "🔥 Популярные игры"
        };

        await RenderGameListAsync(
            callbackQuery,
            chatId,
            title,
            pageResult,
            page,
            $"c{filter}",
            next => $"game:list:{filter}:{next}",
            "game:menu",
            cancellationToken);
    }

    private async Task ShowWantedAsync(
        CallbackQuery? callbackQuery,
        long chatId,
        int page,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Games
            .AsNoTracking()
            .Where(game => game.Copies.Any() && game.Interests.Any())
            .OrderByDescending(game => game.Interests.Count)
            .ThenBy(game => game.Name);

        var pageResult = await ReadPageAsync(query, page, cancellationToken);
        var keyboard = BuildListKeyboard(
            pageResult,
            page,
            "w",
            next => $"game:wanted:{next}",
            "game:menu");
        var rows = keyboard.InlineKeyboard.ToList();
        rows.Insert(0, [InlineKeyboardButton.WithCallbackData("🔥 Мои хотелки", "game:mywanted:0")]);

        await RenderAsync(
            callbackQuery,
            chatId,
            BuildListText("🔥 Больше всего хотят сыграть", pageResult.Items),
            new InlineKeyboardMarkup(rows),
            cancellationToken);
    }

    private async Task ShowMyWantedAsync(
        CallbackQuery? callbackQuery,
        long chatId,
        long telegramUserId,
        int page,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Games
            .AsNoTracking()
            .Where(game => game.Interests.Any(interest =>
                interest.Participant.TelegramUserId == telegramUserId))
            .OrderByDescending(game => game.Interests.Count)
            .ThenBy(game => game.Name);

        var pageResult = await ReadPageAsync(query, page, cancellationToken);
        await RenderGameListAsync(
            callbackQuery,
            chatId,
            "🔥 Мои хотелки",
            pageResult,
            page,
            "mw",
            next => $"game:mywanted:{next}",
            "game:wanted:0",
            cancellationToken);
    }

    private async Task ShowMyGamesMenuAsync(
        CallbackQuery? callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        await RenderAsync(
            callbackQuery,
            chatId,
            "🎒 Мои игры",
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("🔥 Самые востребованные", "game:my:d:0")],
                [InlineKeyboardButton.WithCallbackData("✅ Возьму", "game:my:b:0")],
                [InlineKeyboardButton.WithCallbackData("🤔 Возможно", "game:my:m:0")],
                [InlineKeyboardButton.WithCallbackData("🔎 Поиск", "game:search:my")],
                [InlineKeyboardButton.WithCallbackData("⬅️ Каталог", "game:menu")]
            ]),
            cancellationToken);
    }

    private async Task ShowMyGamesPageAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long telegramUserId,
        string filter,
        int page,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Games
            .AsNoTracking()
            .Where(game => game.Copies.Any(copy =>
                copy.Source == GameCopySource.Personal
                && copy.OwnerParticipant != null
                && copy.OwnerParticipant.TelegramUserId == telegramUserId));

        query = filter switch
        {
            "b" => query.Where(game => game.Copies.Any(copy =>
                copy.OwnerParticipant != null
                && copy.OwnerParticipant.TelegramUserId == telegramUserId
                && copy.BringStatus == BringStatus.Bringing)),
            "m" => query.Where(game => game.Copies.Any(copy =>
                copy.OwnerParticipant != null
                && copy.OwnerParticipant.TelegramUserId == telegramUserId
                && copy.BringStatus == BringStatus.Maybe)),
            _ => query
        };

        var ordered = filter == "d"
            ? query.OrderByDescending(game => game.Interests.Count).ThenBy(game => game.Name)
            : query.OrderBy(game => game.Name);
        var pageResult = await ReadPageAsync(ordered, page, cancellationToken);
        var title = filter switch
        {
            "b" => "✅ Я возьму",
            "m" => "🤔 Возможно возьму",
            _ => "🔥 Мои самые востребованные"
        };

        await RenderGameListAsync(
            callbackQuery,
            chatId,
            title,
            pageResult,
            page,
            $"m{filter}",
            next => $"game:my:{filter}:{next}",
            "game:my:menu",
            cancellationToken);
    }

    private async Task HandleConversationTextAsync(
        long chatId,
        long telegramUserId,
        string state,
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

        if (state == AddSearchState)
        {
            await ClearConversationStateAsync(telegramUserId, cancellationToken);
            await HandleManualAddSearchAsync(chatId, telegramUserId, text, cancellationToken);
            return;
        }

        await ClearConversationStateAsync(telegramUserId, cancellationToken);
        var mineOnly = state == MyGamesSearchState;
        var ids = await searchService.SearchCatalogIdsAsync(
            text,
            mineOnly ? telegramUserId : null,
            cancellationToken);

        if (ids.Count == 0)
        {
            await botClient.SendMessage(
                chatId,
                "Ничего не найдено.",
                cancellationToken: cancellationToken);
            return;
        }

        var games = await dbContext.Games
            .AsNoTracking()
            .Where(game => ids.Contains(game.Id))
            .Select(game => new GameListItem(game.Id, game.Name, game.Interests.Count))
            .ToArrayAsync(cancellationToken);
        var byId = games.ToDictionary(game => game.Id);
        var ordered = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        var context = mineOnly ? "md" : "cp";
        var rows = ordered.Select(game => (IEnumerable<InlineKeyboardButton>)[
            InlineKeyboardButton.WithCallbackData(
                $"{game.Name} · 🔥 {game.InterestCount}",
                $"game:card:{game.Id}:{context}:0")
        ]).ToList();
        rows.Add([
            InlineKeyboardButton.WithCallbackData(
                "⬅️ Назад",
                mineOnly ? "game:my:menu" : "game:menu")
        ]);

        await botClient.SendMessage(
            chatId,
            $"🔎 Результаты поиска: {text}",
            replyMarkup: new InlineKeyboardMarkup(rows),
            cancellationToken: cancellationToken);
    }

    private async Task HandleManualAddSearchAsync(
        long chatId,
        long telegramUserId,
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            if (TryParseBggId(text, out var bggId))
            {
                var externalGame = await searchService.GetBggGameAsync(bggId, cancellationToken);
                if (externalGame is null)
                {
                    await botClient.SendMessage(
                        chatId,
                        "Не удалось найти эту игру на BGG.",
                        cancellationToken: cancellationToken);
                    return;
                }

                var game = await dedupService.FindOrCreateAsync(externalGame, cancellationToken);
                await SendChooseBringStatusAsync(chatId, game.Id, game.Name, cancellationToken);
                return;
            }

            var results = await searchService.SearchExternalAsync(text, cancellationToken);
            if (results.Count == 0)
            {
                await botClient.SendMessage(
                    chatId,
                    "BGG ничего не нашёл. Попробуйте другое название или ссылку BGG.",
                    cancellationToken: cancellationToken);
                return;
            }

            var rows = results.Select(result =>
            {
                var suffix = result.YearPublished is { } year ? $" ({year})" : string.Empty;
                return (IEnumerable<InlineKeyboardButton>)[
                    InlineKeyboardButton.WithCallbackData(
                        $"{result.Name}{suffix}",
                        $"game:add:{result.BggId}")
                ];
            }).ToArray();

            await botClient.SendMessage(
                chatId,
                "Выберите игру:",
                replyMarkup: new InlineKeyboardMarkup(rows),
                cancellationToken: cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "BGG manual game lookup failed.");
            await botClient.SendMessage(
                chatId,
                "BGG временно недоступен. Попробуйте ещё раз позже.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task AddBggCandidateAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long bggId,
        CancellationToken cancellationToken)
    {
        try
        {
            var externalGame = await searchService.GetBggGameAsync(bggId, cancellationToken);
            if (externalGame is null)
            {
                await RenderAsync(
                    callbackQuery,
                    chatId,
                    "Не удалось загрузить игру с BGG.",
                    new InlineKeyboardMarkup([
                        [InlineKeyboardButton.WithCallbackData("⬅️ Каталог", "game:menu")]
                    ]),
                    cancellationToken);
                return;
            }

            var game = await dedupService.FindOrCreateAsync(externalGame, cancellationToken);
            await RenderAsync(
                callbackQuery,
                chatId,
                $"{game.Name}\n\nВы возьмёте эту игру с собой?",
                BringStatusKeyboard(game.Id),
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "BGG game lookup failed for {BggId}.", bggId);
            await RenderAsync(
                callbackQuery,
                chatId,
                "BGG временно недоступен. Попробуйте ещё раз позже.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ Каталог", "game:menu")]
                ]),
                cancellationToken);
        }
    }

    private async Task SendChooseBringStatusAsync(
        long chatId,
        long gameId,
        string gameName,
        CancellationToken cancellationToken) =>
        await botClient.SendMessage(
            chatId,
            $"{gameName}\n\nВы возьмёте эту игру с собой?",
            replyMarkup: BringStatusKeyboard(gameId),
            cancellationToken: cancellationToken);

    private static InlineKeyboardMarkup BringStatusKeyboard(long gameId) =>
        new([
            [InlineKeyboardButton.WithCallbackData("✅ Возьму", $"copy:add:{gameId}:b")],
            [InlineKeyboardButton.WithCallbackData("🤔 Возможно", $"copy:add:{gameId}:m")]
        ]);

    private async Task ShowStatusConfirmationAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        long copyId,
        BringStatus targetStatus,
        CancellationToken cancellationToken)
    {
        var copy = await dbContext.GameCopies
            .AsNoTracking()
            .Include(value => value.Game)
            .Include(value => value.OwnerParticipant)
            .SingleOrDefaultAsync(value =>
                value.Id == copyId
                && value.Source == GameCopySource.Personal
                && value.OwnerParticipant != null
                && value.OwnerParticipant.TelegramUserId == telegramUserId,
                cancellationToken);

        if (copy is null)
        {
            await RenderAsync(
                callbackQuery,
                telegramUserId,
                "Эту копию игры нельзя изменить.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ Мои игры", "game:my:menu")]
                ]),
                cancellationToken);
            return;
        }

        var label = targetStatus == BringStatus.Bringing ? "«Возьму»" : "«Возможно»";
        await RenderAsync(
            callbackQuery,
            telegramUserId,
            $"{copy.Game.Name}\n\nИзменить статус на {label}?",
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData(
                    "Да",
                    $"copy:set:{copy.Id}:{FormatBringStatus(targetStatus)}")],
                [InlineKeyboardButton.WithCallbackData(
                    "Нет",
                    $"game:card:{copy.GameId}:mg:0")]
            ]),
            cancellationToken);
    }

    private async Task ChangeOwnedCopyStatusAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        long copyId,
        BringStatus bringStatus,
        CancellationToken cancellationToken)
    {
        var copy = await dbContext.GameCopies
            .Include(value => value.OwnerParticipant)
            .SingleOrDefaultAsync(value =>
                value.Id == copyId
                && value.Source == GameCopySource.Personal
                && value.OwnerParticipant != null
                && value.OwnerParticipant.TelegramUserId == telegramUserId,
                cancellationToken);

        if (copy is null)
        {
            await RenderAsync(
                callbackQuery,
                telegramUserId,
                "Эту копию игры нельзя изменить.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ Мои игры", "game:my:menu")]
                ]),
                cancellationToken);
            return;
        }

        copy.BringStatus = bringStatus;
        await dbContext.SaveChangesAsync(cancellationToken);

        await ShowGameCardAsync(
            callbackQuery,
            telegramUserId,
            copy.GameId,
            "mg",
            0,
            cancellationToken);
    }

    private async Task StartSearchAsync(
        long chatId,
        long telegramUserId,
        string state,
        string prompt,
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
                State = state,
                ExpiresAt = now.AddMinutes(30),
                UpdatedAt = now
            });
        }
        else
        {
            conversationState.State = state;
            conversationState.DataJson = null;
            conversationState.ExpiresAt = now.AddMinutes(30);
            conversationState.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await botClient.SendMessage(chatId, prompt, cancellationToken: cancellationToken);
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

    private async Task RenderGameListAsync(
        CallbackQuery? callbackQuery,
        long chatId,
        string title,
        PageResult pageResult,
        int page,
        string context,
        Func<int, string> pageCallback,
        string backCallback,
        CancellationToken cancellationToken) =>
        await RenderAsync(
            callbackQuery,
            chatId,
            BuildListText(title, pageResult.Items),
            BuildListKeyboard(pageResult, page, context, pageCallback, backCallback),
            cancellationToken);

    private static string BuildListText(string title, IReadOnlyList<GameListItem> items)
    {
        if (items.Count == 0)
        {
            return $"{title}\n\nПока пусто.";
        }

        var text = new StringBuilder(title).AppendLine().AppendLine();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            text.AppendLine($"{index + 1}. {item.Name} — 🔥 {item.InterestCount}");
        }

        return text.ToString().TrimEnd();
    }

    private static InlineKeyboardMarkup BuildListKeyboard(
        PageResult pageResult,
        int page,
        string context,
        Func<int, string> pageCallback,
        string backCallback)
    {
        var rows = pageResult.Items
            .Select(item => (IEnumerable<InlineKeyboardButton>)[
                InlineKeyboardButton.WithCallbackData(
                    item.Name,
                    $"game:card:{item.Id}:{context}:{page}")
            ])
            .ToList();

        var pagination = new List<InlineKeyboardButton>();
        if (page > 0)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData("⬅️", pageCallback(page - 1)));
        }

        if (pageResult.HasMore)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData("Ещё ➡️", pageCallback(page + 1)));
        }

        if (pagination.Count > 0)
        {
            rows.Add(pagination);
        }

        rows.Add([InlineKeyboardButton.WithCallbackData("⬅️ Назад", backCallback)]);
        return new InlineKeyboardMarkup(rows);
    }

    private async Task RenderAsync(
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

    private static string BuildBackCallback(string context, int page) => context switch
    {
        "cb" => $"game:list:b:{page}",
        "cm" => $"game:list:m:{page}",
        "w" => $"game:wanted:{page}",
        "mw" => $"game:mywanted:{page}",
        "mb" => $"game:my:b:{page}",
        "mm" => $"game:my:m:{page}",
        "md" or "mg" => $"game:my:d:{page}",
        _ => $"game:list:p:{page}"
    };

    private static string FormatPlayers(int? minPlayers, int? maxPlayers) =>
        (minPlayers, maxPlayers) switch
        {
            (null, null) => "—",
            ({ } min, { } max) when min == max => min.ToString(),
            ({ } min, { } max) => $"{min}–{max}",
            ({ } min, null) => $"от {min}",
            (null, { } max) => $"до {max}"
        };

    private static bool TryParseBringStatus(string value, out BringStatus bringStatus)
    {
        switch (value)
        {
            case "b":
                bringStatus = BringStatus.Bringing;
                return true;
            case "m":
                bringStatus = BringStatus.Maybe;
                return true;
            default:
                bringStatus = default;
                return false;
        }
    }

    private static string FormatBringStatus(BringStatus bringStatus) =>
        bringStatus == BringStatus.Bringing ? "b" : "m";

    private static bool TryParseBggId(string value, out long bggId)
    {
        var match = BggUrlRegex().Match(value.Trim());
        return match.Success && long.TryParse(match.Groups[1].Value, out bggId);
    }

    [GeneratedRegex(
        @"(?:https?://)?(?:www\.)?boardgamegeek\.com/boardgame/(\d+)(?:/[^\s]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BggUrlRegex();

    private sealed record GameListItem(long Id, string Name, int InterestCount);
    private sealed record PageResult(IReadOnlyList<GameListItem> Items, bool HasMore);
}
