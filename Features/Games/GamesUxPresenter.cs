using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Games;

public sealed class GamesUxPresenter(
    AppDbContext dbContext,
    ITelegramBotClient botClient)
{
    private const int PageSize = 10;

    public Task ShowCatalogHomeAsync(long chatId, CancellationToken cancellationToken) =>
        RenderAsync(
            null,
            chatId,
            """
            🎲 Игры

            Выберите, что хотите посмотреть.
            """,
            new InlineKeyboardMarkup([
                [
                    InlineKeyboardButton.WithCallbackData("🔥 Популярные", "game:list:p:0"),
                    InlineKeyboardButton.WithCallbackData("✅ Точно будут", "game:list:b:0")
                ],
                [
                    InlineKeyboardButton.WithCallbackData("🤔 Возможно", "game:list:m:0"),
                    InlineKeyboardButton.WithCallbackData("🔎 Поиск", "game:search:catalog")
                ],
                [InlineKeyboardButton.WithCallbackData("📚 Коллекции участников", "game:collections:0")]
            ]),
            cancellationToken);

    public Task ShowWishlistHomeAsync(
        long chatId,
        long telegramUserId,
        CancellationToken cancellationToken) =>
        ShowWishlistHomeAsync(null, chatId, telegramUserId, cancellationToken);

    public async Task<bool> TryHandleCallbackAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrWhiteSpace(data) || callbackQuery.Message is null)
        {
            return false;
        }

        var chatId = callbackQuery.Message.Chat.Id;
        if (data == "game:menu")
        {
            await ShowCatalogHomeAsync(callbackQuery, chatId, cancellationToken);
            return true;
        }

        if (data == "game:my:menu")
        {
            await ShowMyGamesHomeAsync(callbackQuery, chatId, cancellationToken);
            return true;
        }

        if (data == "game:wishlist:menu")
        {
            await ShowWishlistHomeAsync(
                callbackQuery,
                chatId,
                telegramUserId,
                cancellationToken);
            return true;
        }

        var parts = data.Split(':');
        if (parts is ["game", "wishlist", var scope, var pageText]
            && int.TryParse(pageText, out var page)
            && page >= 0)
        {
            await ShowWishlistPageAsync(
                callbackQuery,
                chatId,
                telegramUserId,
                scope,
                page,
                cancellationToken);
            return true;
        }

        if (parts is ["game", "card", var gameIdText, var context, var cardPage]
            && long.TryParse(gameIdText, out var gameId)
            && int.TryParse(cardPage, out page)
            && page >= 0)
        {
            await ShowGameCardAsync(
                callbackQuery,
                telegramUserId,
                gameId,
                context,
                page,
                cancellationToken);
            return true;
        }

        if (parts is ["game", "availability", var availabilityGameId, var availabilityContext, var availabilityPage]
            && long.TryParse(availabilityGameId, out gameId)
            && int.TryParse(availabilityPage, out page)
            && page >= 0)
        {
            await ShowAvailabilityAsync(
                callbackQuery,
                gameId,
                availabilityContext,
                page,
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
                "Игра больше не доступна.",
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("← К играм", "game:menu")]
                ]),
                cancellationToken);
            return;
        }

        var confirmedCopies = game.Copies.Count(copy =>
            copy.Source == GameCopySource.Club
            || copy.BringStatus == BringStatus.Bringing);
        var maybeCopies = game.Copies.Count(copy =>
            copy.Source == GameCopySource.Personal
            && copy.BringStatus == BringStatus.Maybe);

        var text = new StringBuilder()
            .AppendLine($"🎲 <b>{Encode(game.Name)}</b>")
            .AppendLine()
            .AppendLine($"👥 {FormatPlayers(game.MinPlayers, game.MaxPlayers)} · лучше всего: {Encode(game.BestPlayers ?? "—")}")
            .AppendLine($"🔥 Хотят сыграть: {game.Interests.Count}")
            .AppendLine();

        if (game.Copies.Count == 0)
        {
            text.AppendLine("📦 На кэмпе: копий пока нет");
        }
        else
        {
            text.AppendLine("📦 На кэмпе:");
            if (confirmedCopies > 0)
            {
                text.AppendLine($"✅ Точно будут: {confirmedCopies}");
            }
            if (maybeCopies > 0)
            {
                text.AppendLine($"🤔 Возможно: {maybeCopies}");
            }
        }

        var rows = new List<IEnumerable<InlineKeyboardButton>>();
        var isInterested = game.Interests.Any(value => value.ParticipantId == participantId);
        rows.Add([
            InlineKeyboardButton.WithCallbackData(
                isInterested ? "✅ В моих хотелках" : "🔥 Хочу сыграть",
                $"interest:toggle:{game.Id}")
        ]);

        if (game.Copies.Count > 0)
        {
            rows.Add([
                InlineKeyboardButton.WithCallbackData(
                    "▶️ Собрать партию",
                    $"session:game:{game.Id}")
            ]);
        }

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

        if (game.Copies.Count > 0)
        {
            rows.Add([
                InlineKeyboardButton.WithCallbackData(
                    "📦 Кто может привезти",
                    $"game:availability:{game.Id}:{context}:{page}")
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
                "← Назад",
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

    private Task ShowCatalogHomeAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken) =>
        RenderAsync(
            callbackQuery,
            chatId,
            """
            🎲 Игры

            Выберите, что хотите посмотреть.
            """,
            new InlineKeyboardMarkup([
                [
                    InlineKeyboardButton.WithCallbackData("🔥 Популярные", "game:list:p:0"),
                    InlineKeyboardButton.WithCallbackData("✅ Точно будут", "game:list:b:0")
                ],
                [
                    InlineKeyboardButton.WithCallbackData("🤔 Возможно", "game:list:m:0"),
                    InlineKeyboardButton.WithCallbackData("🔎 Поиск", "game:search:catalog")
                ],
                [InlineKeyboardButton.WithCallbackData("📚 Коллекции участников", "game:collections:0")]
            ]),
            cancellationToken);

    private Task ShowWishlistHomeAsync(
        CallbackQuery? callbackQuery,
        long chatId,
        long telegramUserId,
        CancellationToken cancellationToken) =>
        RenderAsync(
            callbackQuery,
            chatId,
            """
            🔥 Хотелки

            Смотрите, во что больше всего хотят сыграть на кэмпе, и управляйте своим списком.
            """,
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("🔥 Популярные", "game:wishlist:popular:0")],
                [InlineKeyboardButton.WithCallbackData("❤️ Мои хотелки", "game:wishlist:mine:0")],
                [InlineKeyboardButton.WithCallbackData("🔎 Найти игру", "game:search:catalog")],
                [InlineKeyboardButton.WithCallbackData("← К играм", "game:menu")]
            ]),
            cancellationToken);

    private Task ShowMyGamesHomeAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken) =>
        RenderAsync(
            callbackQuery,
            chatId,
            """
            🎲 Мои игры

            Здесь можно решить, что вы привезёте на кэмп, добавить одну игру или импортировать коллекцию.
            """,
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("🔥 Самые востребованные", "game:my:d:0")],
                [
                    InlineKeyboardButton.WithCallbackData("✅ Возьму", "game:my:b:0"),
                    InlineKeyboardButton.WithCallbackData("🤔 Возможно", "game:my:m:0")
                ],
                [InlineKeyboardButton.WithCallbackData("🔎 Поиск", "game:search:my")],
                [InlineKeyboardButton.WithCallbackData("➕ Добавить игру", "collection:add:single")],
                [InlineKeyboardButton.WithCallbackData("📥 Импорт коллекции", "collection:menu")],
                [InlineKeyboardButton.WithCallbackData("← К играм", "game:menu")]
            ]),
            cancellationToken);

    private async Task ShowWishlistPageAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long telegramUserId,
        string scope,
        int page,
        CancellationToken cancellationToken)
    {
        IQueryable<Game> query = dbContext.Games
            .AsNoTracking()
            .Where(game => game.Interests.Any());

        if (scope == "mine")
        {
            query = query.Where(game => game.Interests.Any(interest =>
                interest.Participant.TelegramUserId == telegramUserId));
        }
        else if (scope != "popular")
        {
            await ShowWishlistHomeAsync(
                callbackQuery,
                chatId,
                telegramUserId,
                cancellationToken);
            return;
        }

        var rows = await query
            .OrderByDescending(game => game.Interests.Count)
            .ThenBy(game => game.Name)
            .Select(game => new GameListItem(game.Id, game.Name, game.Interests.Count))
            .Skip(page * PageSize)
            .Take(PageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > PageSize;
        var items = rows.Take(PageSize).ToArray();
        var title = scope == "mine"
            ? "❤️ Мои хотелки"
            : "🔥 Популярные хотелки";
        var description = scope == "mine"
            ? "Игры, которые вы отметили для себя."
            : "Игры с наибольшим количеством отметок «Хочу сыграть».";

        var text = items.Length == 0
            ? scope == "mine"
                ? $"{title}\n\nПока пусто. Откройте каталог и отметьте игры, в которые хотите сыграть."
                : $"{title}\n\nПока никто не добавил хотелки."
            : string.Join(
                '\n',
                new[] { title, string.Empty, description, string.Empty }
                    .Concat(items.Select((item, index) =>
                        $"{index + 1}. {item.Name} — 🔥 {item.InterestCount}")));

        var context = scope == "mine" ? "wm" : "wp";
        var keyboardRows = items
            .Select(item => (IEnumerable<InlineKeyboardButton>)[
                InlineKeyboardButton.WithCallbackData(
                    item.Name,
                    $"game:card:{item.Id}:{context}:{page}")
            ])
            .ToList();
        var pagination = new List<InlineKeyboardButton>();
        if (page > 0)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData(
                "⬅️",
                $"game:wishlist:{scope}:{page - 1}"));
        }
        if (hasMore)
        {
            pagination.Add(InlineKeyboardButton.WithCallbackData(
                "Ещё ➡️",
                $"game:wishlist:{scope}:{page + 1}"));
        }
        if (pagination.Count > 0)
        {
            keyboardRows.Add(pagination);
        }
        if (scope == "mine" && items.Length == 0)
        {
            keyboardRows.Add([InlineKeyboardButton.WithCallbackData("🎲 Смотреть игры", "game:menu")]);
        }
        keyboardRows.Add([InlineKeyboardButton.WithCallbackData("← Хотелки", "game:wishlist:menu")]);

        await RenderAsync(
            callbackQuery,
            chatId,
            text,
            new InlineKeyboardMarkup(keyboardRows),
            cancellationToken);
    }

    private async Task ShowAvailabilityAsync(
        CallbackQuery callbackQuery,
        long gameId,
        string context,
        int page,
        CancellationToken cancellationToken)
    {
        var game = await dbContext.Games
            .AsNoTracking()
            .Include(value => value.Copies)
                .ThenInclude(value => value.OwnerParticipant)
            .SingleOrDefaultAsync(value => value.Id == gameId, cancellationToken);

        if (game is null)
        {
            await ShowCatalogHomeAsync(callbackQuery, callbackQuery.Message!.Chat.Id, cancellationToken);
            return;
        }

        var text = new StringBuilder()
            .AppendLine($"📦 <b>{Encode(game.Name)}</b>")
            .AppendLine()
            .AppendLine("Кто может привезти:");

        if (game.Copies.Count == 0)
        {
            text.AppendLine("Пока нет доступных копий.");
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
                    text.AppendLine("🏢 Клуб — точно будет");
                    continue;
                }

                var owner = copy.OwnerParticipant is null
                    ? "Участник"
                    : ParticipantPresentation.ToHtmlLink(copy.OwnerParticipant);
                var status = copy.BringStatus == BringStatus.Bringing ? "✅" : "🤔";
                text.AppendLine($"{status} {owner}");
            }
        }

        await RenderAsync(
            callbackQuery,
            callbackQuery.Message!.Chat.Id,
            text.ToString().TrimEnd(),
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData(
                    "← К игре",
                    $"game:card:{game.Id}:{context}:{page}")]
            ]),
            cancellationToken,
            ParseMode.Html);
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
            "wp" => $"game:wishlist:popular:{page}",
            "wm" => $"game:wishlist:mine:{page}",
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

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private sealed record GameListItem(long Id, string Name, int InterestCount);
}
