using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Games;

public sealed class GamesHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    GameSearchService searchService,
    GameDedupService dedupService,
    ILogger<GamesHandler> logger)
{
    private const int PageSize = 10;
    private const int TelegramTextChunkLimit = 3500;
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
            if (message.Chat.Type != ChatType.Private)
            {
                return true;
            }

            await HandleConversationTextAsync(
                message.Chat.Id,
                telegramUserId,
                conversationState.State,
                text,
                cancellationToken);
            return true;
        }

        var isRecognized = text is "🎲 Игры"
            or "/games"
            or "➕ Добавить игры"
            or "/addgame"
            or "🔥 Хочу сыграть"
            or "/wanted"
            or "Мои игры"
            or "🎲 Мои игры"
            or "/mygames"
            or "Мои хотелки"
            or "🔥 Мои хотелки";
        if (isRecognized && message.Chat.Type != ChatType.Private)
        {
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
                    BuildManualAddPrompt(),
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

        if (callbackQuery.Message?.Chat.Type != ChatType.Private)
        {
            return data.StartsWith("game:", StringComparison.Ordinal)
                || data.StartsWith("copy:", StringComparison.Ordinal);
        }

        var chatId = callbackQuery.Message.Chat.Id;
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

        if (parts is ["game", "collections", var participantsPage]
            && int.TryParse(participantsPage, out var participantPage))
        {
            await ShowParticipantCollectionsAsync(
                callbackQuery,
                chatId,
                Math.Max(participantPage, 0),
                cancellationToken);
            return true;
        }

        if (parts is ["game", "collection", var participantIdText, var collectionPage]
            && long.TryParse(participantIdText, out var participantId)
            && int.TryParse(collectionPage, out var participantCollectionPage))
        {
            await ShowParticipantCollectionAsync(
                callbackQuery,
                chatId,
                participantId,
                Math.Max(participantCollectionPage, 0),
                cancellationToken);
            return true;
        }

        if (parts is ["game", "collectionall", var allParticipantId]
            && long.TryParse(allParticipantId, out participantId))
        {
            await SendFullParticipantCollectionAsync(
                callbackQuery,
                chatId,
                participantId,
                cancellationToken);
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
                : "Введите часть названия уже добавленной игры в каталоге.";

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
        text.AppendLine($"🎲 <b>{Encode(game.Name)}</b>");
        text.AppendLine();
        text.AppendLine($"👥 Игроки: {FormatPlayers(game.MinPlayers, game.MaxPlayers)}");
        text.AppendLine($"⭐ Лучше всего: {Encode(game.BestPlayers ?? "—")}");
        text.AppendLine($"🔥 Хотят сыграть: {game.Interests.Count}");
        text.AppendLine();
        text.AppendLine("📦 Доступность:");

        if (game.Copies.Count == 0)
        {
            text.AppendLine("— пока нет доступных копий");
        }
        else
        {
            foreach (var copy in game.Copies
                         .OrderBy(value => value.Source)
                         .ThenBy(value => value.OwnerParticipant is null
                             ? string.Empty
                             : ParticipantPresentation.GetDisplayName(value.OwnerParticipant)))
            {
                if (copy.Source == GameCopySource.Club)
                {
                    text.AppendLine("🏢 Клуб — будет на мероприятии");
                    continue;
                }

                var owner = copy.OwnerParticipant is null
                    ? "Участник"
                    : ParticipantPresentation.ToHtmlLink(copy.OwnerParticipant);
                var status = copy.BringStatus == BringStatus.Bringing
                    ? $"✅ {owner} — возьмёт с собой"
                    : $"🤔 {owner} — пока не решил";
                text.AppendLine(status);
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
            rows.Add([
                InlineKeyboardButton.WithUrl(
                    GameExternalLinkLabel.ForUrl(game.ExternalUrl),
                    game.ExternalUrl)
            ]);
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
            cancellationToken,
            ParseMode.Html);
    }

    private async Task ShowCatalogMenuAsync(long chatId, CancellationToken cancellationToken) =>
        await botClient.SendMessage(
            chatId,
            CatalogMenuText(),
            replyMarkup: CatalogMenuKeyboard(),
            cancellationToken: cancellationToken);

    private async Task ShowCatalogMenuAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken) =>
        await RenderAsync(
            callbackQuery,
            chatId,
            CatalogMenuText(),
            CatalogMenuKeyboard(),
            cancellationToken);

    private static string CatalogMenuText() => """
        🎲 Игры

        🔥 Популярные
        Игры с самым высоким спросом среди участников.

        ✅ Точно будут
        Игры клуба и личные игры, которые владельцы подтвердили.

        🤔 Возможно будут
        Игры есть у участников, но они ещё не подтвердили, что возьмут их с собой.

        📚 Коллекции участников
        Посмотреть игры конкретного участника.

        🎒 Мои игры
        Управлять своими играми и их статусом.
        """;

    private static InlineKeyboardMarkup CatalogMenuKeyboard() =>
        new([
            [InlineKeyboardButton.WithCallbackData("🔥 Популярные — спрос", "game:list:p:0")],
            [InlineKeyboardButton.WithCallbackData("✅ Точно будут", "game:list:b:0")],
            [InlineKeyboardButton.WithCallbackData("🤔 Возможно будут", "game:list:m:0")],
            [InlineKeyboardButton.WithCallbackData("📚 Коллекции участников", "game:collections:0")],
            [InlineKeyboardButton.WithCallbackData("🔎 Поиск по каталогу", "game:search:catalog")],
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
            "b" => "✅ Точно будут\n\nКлубные игры и личные игры, которые владельцы подтвердили.",
            "m" => "🤔 Возможно будут\n\nИгры есть у участников, но владельцы ещё не подтвердили, что возьмут их с собой.",
            _ => "🔥 Популярные по спросу\n\nРейтинг по количеству отметок «Хочу сыграть». Это спрос, а не подтверждение доступности игры."
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
            BuildListText(
                "🔥 Хочу сыграть\n\nОбщий рейтинг спроса. Откройте игру, чтобы поставить или убрать свою отметку. Отметка не означает, что вы обязуетесь взять игру с собой.",
                pageResult.Items),
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
            "🔥 Мои хотелки\n\nИгры, которые именно вы отметили «Хочу сыграть».",
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
            """
            🎒 Мои игры

            Здесь только ваши личные игры — добавленные вручную или импортированные из личной коллекции.

            ✅ «Возьму»
            Вы подтверждаете, что возьмёте игру с собой на Настолкомарафон-2026.

            🤔 «Возможно»
            Игра у вас есть, но вы ещё не решили, будете ли брать её с собой.

            🔥 «Самые востребованные»
            Ваши игры, отсортированные по общему спросу участников.
            """,
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
            "b" => "✅ Я возьму\n\nЭти игры вы подтвердили — они будут с вами на мероприятии.",
            "m" => "🤔 Возможно возьму\n\nЭти игры у вас есть, но вы ещё не решили, брать ли их с собой.",
            _ => "🔥 Мои самые востребованные\n\nВаши игры, отсортированные по спросу других участников."
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

    private async Task ShowParticipantCollectionsAsync(
        CallbackQuery callbackQuery,
        long chatId,
        int page,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Participants
            .AsNoTracking()
            .Where(participant => participant.GameCopies.Any(copy => copy.Source == GameCopySource.Personal))
            .OrderBy(participant => participant.PreferredDisplayName ?? participant.DisplayName)
            .ThenBy(participant => participant.Id)
            .Select(participant => new ParticipantCollectionItem(
                participant.Id,
                participant.PreferredDisplayName ?? participant.DisplayName,
                participant.GameCopies.Count(copy => copy.Source == GameCopySource.Personal)))
            .Skip(page * PageSize)
            .Take(PageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > PageSize;
        var items = rows.Take(PageSize).ToArray();

        var keyboardRows = items
            .Select(item => (IEnumerable<InlineKeyboardButton>)[
                InlineKeyboardButton.WithCallbackData(
                    $"{item.DisplayName} · {item.GameCount}",
                    $"game:collection:{item.ParticipantId}:0")
            ])
            .ToList();
        var pagination = new List<InlineKeyboardButton>();
        if (page > 0)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData("⬅️", $"game:collections:{page - 1}"));
        }
        if (hasMore)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData("Ещё ➡️", $"game:collections:{page + 1}"));
        }
        if (pagination.Count > 0)
        {
            keyboardRows.Add(pagination);
        }
        keyboardRows.Add([InlineKeyboardButton.WithCallbackData("⬅️ Каталог", "game:menu")]);

        var text = items.Length == 0
            ? "📚 Коллекции участников\n\nПока никто не добавил личные игры."
            : "📚 Коллекции участников\n\nВыберите участника.\n\nПоказываются только люди, у которых есть личные игры. Внутри можно открыть карточки или получить весь список отдельным сообщением.";

        await RenderAsync(
            callbackQuery,
            chatId,
            text,
            new InlineKeyboardMarkup(keyboardRows),
            cancellationToken);
    }

    private async Task ShowParticipantCollectionAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long participantId,
        int page,
        CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants
            .AsNoTracking()
            .Where(value => value.Id == participantId
                && value.GameCopies.Any(copy => copy.Source == GameCopySource.Personal))
            .Select(value => new
            {
                value.Id,
                DisplayName = value.PreferredDisplayName ?? value.DisplayName
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (participant is null)
        {
            await RenderAsync(
                callbackQuery,
                chatId,
                "Коллекция участника больше недоступна.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("⬅️ К участникам", "game:collections:0")]
                ]),
                cancellationToken);
            return;
        }

        var rows = await dbContext.GameCopies
            .AsNoTracking()
            .Where(copy => copy.Source == GameCopySource.Personal
                && copy.OwnerParticipantId == participantId)
            .OrderBy(copy => copy.Game.Name)
            .Select(copy => new ParticipantGameItem(
                copy.GameId,
                copy.Game.Name,
                copy.BringStatus,
                copy.Game.Interests.Count))
            .Skip(page * PageSize)
            .Take(PageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > PageSize;
        var items = rows.Take(PageSize).ToArray();

        var text = new StringBuilder($"📚 Коллекция: {participant.DisplayName}")
            .AppendLine()
            .AppendLine()
            .AppendLine("✅ — владелец возьмёт с собой")
            .AppendLine("🤔 — владелец пока не решил")
            .AppendLine();
        if (items.Length == 0)
        {
            text.Append("Пока пусто.");
        }
        else
        {
            foreach (var item in items)
            {
                text.AppendLine($"{BringEmoji(item.BringStatus)} {item.Name} — 🔥 {item.InterestCount}");
            }
        }

        var keyboardRows = items
            .Select(item => (IEnumerable<InlineKeyboardButton>)[
                InlineKeyboardButton.WithCallbackData(
                    $"{BringEmoji(item.BringStatus)} {item.Name}",
                    $"game:card:{item.GameId}:u{participantId}:{page}")
            ])
            .ToList();
        var pagination = new List<InlineKeyboardButton>();
        if (page > 0)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData(
                "⬅️",
                $"game:collection:{participantId}:{page - 1}"));
        }
        if (hasMore)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData(
                "Ещё ➡️",
                $"game:collection:{participantId}:{page + 1}"));
        }
        if (pagination.Count > 0)
        {
            keyboardRows.Add(pagination);
        }
        keyboardRows.Add([
            InlineKeyboardButton.WithCallbackData(
                "📄 Получить весь список",
                $"game:collectionall:{participantId}")
        ]);
        keyboardRows.Add([InlineKeyboardButton.WithCallbackData("⬅️ К участникам", "game:collections:0")]);

        await RenderAsync(
            callbackQuery,
            chatId,
            text.ToString().TrimEnd(),
            new InlineKeyboardMarkup(keyboardRows),
            cancellationToken);
    }

    private async Task SendFullParticipantCollectionAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long participantId,
        CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == participantId, cancellationToken);
        if (participant is null)
        {
            return;
        }

        var games = await dbContext.GameCopies
            .AsNoTracking()
            .Where(copy => copy.Source == GameCopySource.Personal
                && copy.OwnerParticipantId == participantId)
            .OrderBy(copy => copy.Game.Name)
            .Select(copy => new { copy.Game.Name, copy.BringStatus })
            .ToArrayAsync(cancellationToken);
        if (games.Length == 0)
        {
            return;
        }

        var header = $"📄 Полный список — {ParticipantPresentation.ToHtmlLink(participant)}\n\n✅ возьмёт с собой\n🤔 пока не решил\n\n";
        var lines = games
            .Select(game => $"{BringEmoji(game.BringStatus)} {Encode(game.Name)}")
            .ToArray();
        var chunks = SplitLines(header, lines, TelegramTextChunkLimit);
        foreach (var chunk in chunks)
        {
            await botClient.SendMessage(
                chatId,
                chunk,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }

        await RenderAsync(
            callbackQuery,
            chatId,
            $"📚 Коллекция: {ParticipantPresentation.GetDisplayName(participant)}\n\nПолный список отправлен отдельными сообщениями ниже. Его удобно находить позже через поиск Telegram.",
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("⬅️ К коллекции", $"game:collection:{participantId}:0")]
            ]),
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
            $"🔎 Результаты поиска\n\n{text}",
            replyMarkup: new InlineKeyboardMarkup(rows),
            cancellationToken: cancellationToken);
    }

    private async Task HandleManualAddSearchAsync(
        long chatId,
        long telegramUserId,
        string text,
        CancellationToken cancellationToken)
    {
        if (TryParseBggId(text, out var bggId))
        {
            var existingGame = await dbContext.Games
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.BggId == bggId, cancellationToken);
            if (existingGame is not null)
            {
                await SendChooseBringStatusAsync(
                    chatId,
                    existingGame.Id,
                    existingGame.Name,
                    cancellationToken);
                return;
            }

            if (!searchService.IsBggAvailable)
            {
                await botClient.SendMessage(
                    chatId,
                    $"Ссылку BGG распознал: ID {bggId}.\n\nЭтой игры ещё нет в каталоге, а без BGG API нельзя надёжно получить её название и метаданные. Ничего не добавлено.",
                    replyMarkup: new InlineKeyboardMarkup([
                        [InlineKeyboardButton.WithCallbackData("← К добавлению игр", "collection:menu")]
                    ]),
                    cancellationToken: cancellationToken);
                return;
            }

            try
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
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "BGG manual game lookup failed for {BggId}.", bggId);
                await botClient.SendMessage(
                    chatId,
                    "BGG временно недоступен.\n\nИгра не добавлена без метаданных.",
                    cancellationToken: cancellationToken);
                return;
            }
        }

        if (!searchService.IsBggAvailable)
        {
            await botClient.SendMessage(
                chatId,
                "Поиск новой игры по названию использует BGG и сейчас недоступен.\n\nСсылку BGG всё ещё можно прислать: если игра уже есть в каталоге, бот добавит её без нового запроса к BGG.",
                replyMarkup: new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("← К добавлению игр", "collection:menu")]
                ]),
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            var results = await searchService.SearchExternalAsync(text, cancellationToken);
            if (results.Count == 0)
            {
                await botClient.SendMessage(
                    chatId,
                    "BGG ничего не нашёл.\n\nПопробуйте другое название или ссылку BGG.",
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
            }).ToList();
            rows.Add([InlineKeyboardButton.WithCallbackData("← К добавлению игр", "collection:menu")]);

            await botClient.SendMessage(
                chatId,
                "Выберите игру:",
                replyMarkup: new InlineKeyboardMarkup(rows),
                cancellationToken: cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "BGG manual game search failed.");
            await botClient.SendMessage(
                chatId,
                "BGG временно недоступен. Попробуйте позже.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task AddBggCandidateAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long bggId,
        CancellationToken cancellationToken)
    {
        var existingGame = await dbContext.Games
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.BggId == bggId, cancellationToken);
        if (existingGame is not null)
        {
            await RenderAsync(
                callbackQuery,
                chatId,
                $"{existingGame.Name}\n\nВы возьмёте эту игру с собой?",
                BringStatusKeyboard(existingGame.Id),
                cancellationToken);
            return;
        }

        if (!searchService.IsBggAvailable)
        {
            await RenderAsync(
                callbackQuery,
                chatId,
                "BGG API сейчас недоступен, поэтому нельзя загрузить метаданные выбранной новой игры. Повторите после проверки BGG_API_TOKEN.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("← К добавлению игр", "collection:menu")]
                ]),
                cancellationToken);
            return;
        }

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
                        [InlineKeyboardButton.WithCallbackData("← К добавлению игр", "collection:menu")]
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
                "BGG временно недоступен. Попробуйте позже.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("← К добавлению игр", "collection:menu")]
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

        if (copy.BringStatus != bringStatus)
        {
            copy.BringStatus = bringStatus;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

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
        await botClient.SendMessage(
            chatId,
            prompt,
            replyMarkup: state == AddSearchState
                ? new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("← Назад", "collection:menu")]
                ])
                : null,
            cancellationToken: cancellationToken);
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
        CancellationToken cancellationToken,
        ParseMode parseMode = default)
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

    private string BuildManualAddPrompt() => searchService.IsBggAvailable
        ? "Добавление одной игры\n\nОтправьте ссылку BGG вида boardgamegeek.com/boardgame/12345 или название игры."
        : "Добавление одной игры\n\nСсылку BGG можно прислать: ID распознаётся без API, а уже известную каталогу игру можно добавить себе. Метаданные новой игры без BGG API бот не выдумывает.";

    private static string BuildBackCallback(string context, int page)
    {
        if (context.Length > 1
            && context[0] == 'u'
            && long.TryParse(context[1..], out var participantId))
        {
            return $"game:collection:{participantId}:{page}";
        }

        return context switch
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
    }

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

    private static string BringEmoji(BringStatus bringStatus) =>
        bringStatus == BringStatus.Bringing ? "✅" : "🤔";

    private static bool TryParseBggId(string value, out long bggId)
    {
        var parsed = BggGameUrlParser.Parse(value);
        bggId = parsed.GetValueOrDefault();
        return parsed.HasValue;
    }

    private static IReadOnlyList<string> SplitLines(
        string firstHeader,
        IReadOnlyList<string> lines,
        int maxLength)
    {
        var result = new List<string>();
        var builder = new StringBuilder(firstHeader);
        foreach (var line in lines)
        {
            if (builder.Length + line.Length + 1 > maxLength && builder.Length > 0)
            {
                result.Add(builder.ToString().TrimEnd());
                builder.Clear();
            }

            builder.AppendLine(line);
        }

        if (builder.Length > 0)
        {
            result.Add(builder.ToString().TrimEnd());
        }

        return result;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private sealed record GameListItem(long Id, string Name, int InterestCount);
    private sealed record PageResult(IReadOnlyList<GameListItem> Items, bool HasMore);
    private sealed record ParticipantCollectionItem(long ParticipantId, string DisplayName, int GameCount);
    private sealed record ParticipantGameItem(long GameId, string Name, BringStatus BringStatus, int InterestCount);
}
