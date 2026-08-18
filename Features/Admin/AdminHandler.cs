using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Admin;

public sealed class AdminHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    CsvExportService csvExportService,
    IOptions<CampOptions> campOptions,
    IOptions<BggOptions> bggOptions)
{
    private const decimal AccommodationPricePerDay = 3000m;
    private const string BggUnavailableMessage = "BGG пока недоступен — ждём подтверждение API-доступа.";

    public async Task HandleCommandAsync(
        Message message,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin(telegramUserId))
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "Доступ запрещён.",
                cancellationToken: cancellationToken);
            return;
        }

        await EnsureAdminParticipantStubAsync(
            message.From,
            telegramUserId,
            cancellationToken);

        await botClient.SendMessage(
            telegramUserId,
            "Админ-панель",
            replyMarkup: BuildMenu(),
            cancellationToken: cancellationToken);
    }

    public async Task<bool> TryHandleCallbackAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrWhiteSpace(data)
            || !data.StartsWith("admin:", StringComparison.Ordinal))
        {
            return false;
        }

        if (!IsAdmin(telegramUserId))
        {
            var unauthorizedChatId = callbackQuery.Message?.Chat.Id ?? telegramUserId;
            await botClient.SendMessage(
                unauthorizedChatId,
                "Доступ запрещён.",
                cancellationToken: cancellationToken);
            return true;
        }

        await EnsureAdminParticipantStubAsync(
            callbackQuery.From,
            telegramUserId,
            cancellationToken);

        switch (data)
        {
            case "admin:menu":
                await SendOrEditAsync(
                    callbackQuery,
                    telegramUserId,
                    "Админ-панель",
                    BuildMenu(),
                    cancellationToken);
                break;

            case "admin:participants":
                await ShowParticipantsAsync(callbackQuery, telegramUserId, cancellationToken);
                break;

            case "admin:accommodation":
                await ShowAccommodationAsync(callbackQuery, telegramUserId, cancellationToken);
                break;

            case "admin:games":
                await ShowGamesAsync(callbackQuery, telegramUserId, cancellationToken);
                break;

            case "admin:top":
                await ShowTopGamesAsync(callbackQuery, telegramUserId, cancellationToken);
                break;

            case "admin:club":
                await ShowClubCollectionAsync(callbackQuery, telegramUserId, cancellationToken);
                break;

            case "admin:stats":
                await ShowStatisticsAsync(callbackQuery, telegramUserId, cancellationToken);
                break;

            case "admin:export":
                await ExportAsync(callbackQuery, telegramUserId, cancellationToken);
                break;
        }

        return true;
    }

    private async Task ShowParticipantsAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        var query = RegisteredParticipantsQuery();
        var total = await query.CountAsync(cancellationToken);
        var oneDay = await query.CountAsync(value => value.DaysStaying == 1, cancellationToken);
        var twoDays = await query.CountAsync(value => value.DaysStaying == 2, cancellationToken);
        var threeDays = await query.CountAsync(value => value.DaysStaying == 3, cancellationToken);

        var text = $"""
            👥 Участники

            Всего: {total}
            1 день: {oneDay}
            2 дня: {twoDays}
            3 дня: {threeDays}
            """;

        await SendOrEditAsync(
            callbackQuery,
            chatId,
            text,
            BuildBackKeyboard(),
            cancellationToken);
    }

    private async Task ShowAccommodationAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        var query = RegisteredParticipantsQuery()
            .Where(value => value.NeedsAccommodation == true);
        var participantCount = await query.CountAsync(cancellationToken);
        var personDays = await query.SumAsync(
            value => value.DaysStaying ?? 0,
            cancellationToken);
        var estimatedTotal = personDays * AccommodationPricePerDay;

        var text = $"""
            🏠 Жильё

            Нужно жильё: {participantCount}
            Человеко-дней: {personDays}
            Ориентировочно: {estimatedTotal:0} ₸

            Расчёт справочный.
            """;

        await SendOrEditAsync(
            callbackQuery,
            chatId,
            text,
            BuildBackKeyboard(),
            cancellationToken);
    }

    private async Task ShowGamesAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        var uniqueGames = await dbContext.Games.AsNoTracking().CountAsync(cancellationToken);
        var personalCopies = await dbContext.GameCopies
            .AsNoTracking()
            .CountAsync(value => value.Source == GameCopySource.Personal, cancellationToken);
        var clubCopies = await dbContext.GameCopies
            .AsNoTracking()
            .CountAsync(value => value.Source == GameCopySource.Club, cancellationToken);
        var bringingCopies = await dbContext.GameCopies
            .AsNoTracking()
            .CountAsync(value => value.BringStatus == BringStatus.Bringing, cancellationToken);
        var maybeCopies = await dbContext.GameCopies
            .AsNoTracking()
            .CountAsync(value => value.BringStatus == BringStatus.Maybe, cancellationToken);

        var text = $"""
            🎲 Игры

            Уникальных игр: {uniqueGames}
            Копий клуба: {clubCopies}
            Личных копий: {personalCopies}
            Точно будут: {bringingCopies}
            Возможно: {maybeCopies}
            """;

        await SendOrEditAsync(
            callbackQuery,
            chatId,
            text,
            BuildBackKeyboard(),
            cancellationToken);
    }

    private async Task ShowTopGamesAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        var games = await dbContext.Games
            .AsNoTracking()
            .Select(value => new
            {
                value.Name,
                InterestCount = value.Interests.Count()
            })
            .Where(value => value.InterestCount > 0)
            .OrderByDescending(value => value.InterestCount)
            .ThenBy(value => value.Name)
            .Take(10)
            .ToListAsync(cancellationToken);

        var text = games.Count == 0
            ? "🔥 Топ игр\n\nИнтересов пока нет."
            : "🔥 Топ игр\n\n" + string.Join(
                "\n",
                games.Select((game, index) => $"{index + 1}. {game.Name} — 🔥 {game.InterestCount}"));

        await SendOrEditAsync(
            callbackQuery,
            chatId,
            text,
            BuildBackKeyboard(),
            cancellationToken);
    }

    private async Task ShowClubCollectionAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        var rows = new List<IEnumerable<InlineKeyboardButton>>();
        if (bggOptions.Value.IsAvailable)
        {
            rows.Add(
            [
                InlineKeyboardButton.WithCallbackData("BGG", "collection:import:bgg:club"),
                InlineKeyboardButton.WithCallbackData("Tesera", "collection:import:tesera:club")
            ]);
        }
        else
        {
            rows.Add(
            [
                InlineKeyboardButton.WithCallbackData("Tesera", "collection:import:tesera:club")
            ]);
        }

        rows.Add([InlineKeyboardButton.WithCallbackData("← Админ-панель", "admin:menu")]);
        var text = bggOptions.Value.IsAvailable
            ? "🏢 Коллекция клуба\n\nВыберите источник импорта."
            : $"🏢 Коллекция клуба\n\n{BggUnavailableMessage}\nДля импорта сейчас доступна Tesera.";

        await SendOrEditAsync(
            callbackQuery,
            chatId,
            text,
            new InlineKeyboardMarkup(rows),
            cancellationToken);
    }

    private async Task ShowStatisticsAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        var registeredParticipants = await RegisteredParticipantsQuery().CountAsync(cancellationToken);
        var accommodationParticipants = await RegisteredParticipantsQuery()
            .CountAsync(value => value.NeedsAccommodation == true, cancellationToken);
        var uniqueGames = await dbContext.Games.AsNoTracking().CountAsync(cancellationToken);
        var interests = await dbContext.GameInterests.AsNoTracking().CountAsync(cancellationToken);
        var sessions = await dbContext.GameSessions.AsNoTracking().CountAsync(cancellationToken);
        var recruitingSessions = await dbContext.GameSessions
            .AsNoTracking()
            .CountAsync(value => value.Status == SessionStatus.Recruiting, cancellationToken);
        var fullSessions = await dbContext.GameSessions
            .AsNoTracking()
            .CountAsync(value => value.Status == SessionStatus.Full, cancellationToken);
        var closedSessions = await dbContext.GameSessions
            .AsNoTracking()
            .CountAsync(value => value.Status == SessionStatus.Closed, cancellationToken);

        var text = $"""
            📊 Статистика

            Участники: {registeredParticipants}
            Нужно жильё: {accommodationParticipants}
            Уникальных игр: {uniqueGames}
            Хотелок: {interests}
            Сессий всего: {sessions}
            Набор открыт: {recruitingSessions}
            Состав набран: {fullSessions}
            Закрыто: {closedSessions}
            """;

        await SendOrEditAsync(
            callbackQuery,
            chatId,
            text,
            BuildBackKeyboard(),
            cancellationToken);
    }

    private async Task ExportAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        await SendOrEditAsync(
            callbackQuery,
            chatId,
            "📤 Экспорт CSV отправляется отдельными файлами.",
            BuildBackKeyboard(),
            cancellationToken);

        IReadOnlyList<CsvExportFile> exports;
        try
        {
            exports = await csvExportService.CreateAllAsync(chatId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            await botClient.SendMessage(
                chatId,
                "Доступ запрещён.",
                cancellationToken: cancellationToken);
            return;
        }

        foreach (var export in exports)
        {
            using (export.Content)
            {
                await botClient.SendDocument(
                    chatId,
                    InputFile.FromStream(export.Content, export.FileName),
                    cancellationToken: cancellationToken);
            }
        }
    }

    private IQueryable<Participant> RegisteredParticipantsQuery() =>
        dbContext.Participants
            .AsNoTracking()
            .Where(value => value.DaysStaying.HasValue
                && value.DaysStaying.Value >= 1
                && value.DaysStaying.Value <= 3
                && value.NeedsAccommodation.HasValue);

    private async Task EnsureAdminParticipantStubAsync(
        User? user,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var displayName = BuildDisplayName(user, telegramUserId);
        var username = user?.Username;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Participants"
                ("TelegramUserId", "TelegramUsername", "DisplayName", "CreatedAt", "UpdatedAt")
            VALUES
                ({telegramUserId}, {username}, {displayName}, {now}, {now})
            ON CONFLICT ("TelegramUserId") DO NOTHING
            """,
            cancellationToken);
    }

    private static string BuildDisplayName(User? user, long telegramUserId)
    {
        if (user is null)
        {
            return telegramUserId.ToString();
        }

        var displayName = string.Join(
            ' ',
            new[] { user.FirstName, user.LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(displayName)
            ? user.Username ?? telegramUserId.ToString()
            : displayName;
    }

    private bool IsAdmin(long telegramUserId) =>
        campOptions.Value.AdminTelegramIds.Contains(telegramUserId);

    private static InlineKeyboardMarkup BuildMenu() =>
        new(
        [
            [
                InlineKeyboardButton.WithCallbackData("👥 Участники", "admin:participants"),
                InlineKeyboardButton.WithCallbackData("🏠 Жильё", "admin:accommodation")
            ],
            [
                InlineKeyboardButton.WithCallbackData("🎲 Игры", "admin:games"),
                InlineKeyboardButton.WithCallbackData("🔥 Топ игр", "admin:top")
            ],
            [InlineKeyboardButton.WithCallbackData("🏢 Коллекция клуба", "admin:club")],
            [
                InlineKeyboardButton.WithCallbackData("📊 Статистика", "admin:stats"),
                InlineKeyboardButton.WithCallbackData("📤 Export", "admin:export")
            ]
        ]);

    private static InlineKeyboardMarkup BuildBackKeyboard() =>
        new(
        [
            [InlineKeyboardButton.WithCallbackData("← Админ-панель", "admin:menu")]
        ]);

    private async Task SendOrEditAsync(
        CallbackQuery callbackQuery,
        long chatId,
        string text,
        InlineKeyboardMarkup replyMarkup,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.Message is { } callbackMessage
            && callbackMessage.Chat.Id == chatId)
        {
            await botClient.EditMessageText(
                chatId,
                callbackMessage.MessageId,
                text,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(
            chatId,
            text,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }
}
