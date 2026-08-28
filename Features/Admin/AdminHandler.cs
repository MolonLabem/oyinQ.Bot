using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Admin;

public sealed class AdminHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    CsvExportService csvExportService,
    CampCreationService campCreationService,
    IAdministratorStore administratorStore)
{

    public async Task HandleCommandAsync(
        Message message,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        if (!await administratorStore.IsAdministratorAsync(telegramUserId, cancellationToken))
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
            BuildMenuText(),
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

        if (!await administratorStore.IsAdministratorAsync(telegramUserId, cancellationToken))
        {
            await botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "Доступ запрещён.",
                showAlert: true,
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
                    BuildMenuText(),
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

            case "admin:camp:create":
                await BeginCampCreationAsync(callbackQuery, telegramUserId, cancellationToken);
                break;

            case "admin:stats":
                await ShowStatisticsAsync(callbackQuery, telegramUserId, cancellationToken);
                break;

            case "admin:export":
                await ExportAsync(callbackQuery, telegramUserId, cancellationToken);
                break;

            default:
                if (data.StartsWith("admin:camp:source:", StringComparison.Ordinal))
                {
                    await SelectCampSourceAsync(callbackQuery, telegramUserId, data, cancellationToken);
                }
                break;
        }

        return true;
    }

    public async Task<bool> TryHandleMessageAsync(
        Participant participant,
        Message message,
        CancellationToken cancellationToken)
    {
        if (!await administratorStore.IsAdministratorAsync(participant.TelegramUserId, cancellationToken)) return false;
        var state = await dbContext.ParticipantConversationStates
            .SingleOrDefaultAsync(value => value.ParticipantId == participant.Id, cancellationToken);
        if (state is null || !state.State.StartsWith("admin:camp:", StringComparison.Ordinal)) return false;

        if (state.State == "admin:camp:name" && !string.IsNullOrWhiteSpace(message.Text))
        {
            var name = message.Text.Trim();
            if (name.Length > 160)
            {
                await botClient.SendMessage(message.Chat.Id, "Название должно быть не длиннее 160 символов.", cancellationToken: cancellationToken);
                return true;
            }

            state.State = "admin:camp:source";
            state.DataJson = JsonSerializer.Serialize(new CampCreationState(name, null));
            state.UpdatedAt = DateTimeOffset.UtcNow;
            state.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
            await dbContext.SaveChangesAsync(cancellationToken);
            var clubs = await dbContext.Clubs.AsNoTracking().OrderBy(value => value.Name).ToArrayAsync(cancellationToken);
            var rows = clubs.Select(value => new[]
            {
                InlineKeyboardButton.WithCallbackData(value.Name, $"admin:camp:source:{value.Id}")
            }).ToList();
            rows.Add([InlineKeyboardButton.WithCallbackData("Без коллекции клуба", "admin:camp:source:none")]);
            await botClient.SendMessage(
                message.Chat.Id,
                "Использовать снимок коллекции клуба как основу кэмпа?",
                replyMarkup: new InlineKeyboardMarkup(rows),
                cancellationToken: cancellationToken);
            return true;
        }

        if (state.State == "admin:camp:chat" && message.ChatShared is { } sharedChat)
        {
            if (sharedChat.RequestId != 2401)
            {
                await botClient.SendMessage(message.Chat.Id, "Telegram вернул неизвестный запрос выбора группы. Начните создание кэмпа заново.", cancellationToken: cancellationToken);
                return true;
            }
            var data = JsonSerializer.Deserialize<CampCreationState>(state.DataJson ?? "{}")
                ?? throw new InvalidOperationException("Состояние создания кэмпа повреждено.");
            try
            {
                var key = $"camp-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..27];
                var timeZoneId = data.SourceClubId is { } sourceClubId
                    ? await dbContext.Clubs.Where(value => value.Id == sourceClubId)
                        .Select(value => value.BotChat.TimeZoneId)
                        .SingleAsync(cancellationToken)
                    : "Asia/Qyzylorda";
                var camp = await campCreationService.CreateAsync(
                    new CreateCampCommand(
                        key,
                        data.Name,
                        sharedChat.ChatId,
                        timeZoneId,
                        participant.TelegramUserId,
                        data.SourceClubId),
                    cancellationToken);
                dbContext.ParticipantConversationStates.Remove(state);
                await dbContext.SaveChangesAsync(cancellationToken);
                await botClient.SendMessage(
                    message.Chat.Id,
                    $"✅ Кэмп «{camp.Name}» создан. Бот проверил доступ к выбранной группе.",
                    replyMarkup: new ReplyKeyboardRemove(),
                    cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
            {
                await botClient.SendMessage(
                    message.Chat.Id,
                    $"Не удалось создать кэмп: {exception.Message}",
                    cancellationToken: cancellationToken);
            }

            return true;
        }

        return false;
    }

    private async Task BeginCampCreationAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);
        await SetStateAsync(participant.Id, "admin:camp:name", null, cancellationToken);
        await botClient.SendMessage(
            telegramUserId,
            "Введите название нового кэмпа.",
            cancellationToken: cancellationToken);
    }

    private async Task SelectCampSourceAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        string data,
        CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);
        var state = await dbContext.ParticipantConversationStates.SingleOrDefaultAsync(
            value => value.ParticipantId == participant.Id && value.State == "admin:camp:source",
            cancellationToken);
        if (state is null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Сценарий устарел. Начните создание кэмпа заново.", showAlert: true, cancellationToken: cancellationToken);
            return;
        }

        var current = JsonSerializer.Deserialize<CampCreationState>(state.DataJson ?? "{}")
            ?? throw new InvalidOperationException("Состояние создания кэмпа повреждено.");
        var sourceValue = data["admin:camp:source:".Length..];
        long? sourceClubId = null;
        var parsedClubId = 0L;
        if (!string.Equals(sourceValue, "none", StringComparison.Ordinal)
            && !long.TryParse(sourceValue, out parsedClubId))
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Клуб не найден.", showAlert: true, cancellationToken: cancellationToken);
            return;
        }
        else if (!string.Equals(sourceValue, "none", StringComparison.Ordinal))
        {
            sourceClubId = parsedClubId;
        }

        state.State = "admin:camp:chat";
        state.DataJson = JsonSerializer.Serialize(current with { SourceClubId = sourceClubId });
        state.UpdatedAt = DateTimeOffset.UtcNow;
        state.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        await dbContext.SaveChangesAsync(cancellationToken);

        var requestChat = new KeyboardButtonRequestChat
        {
            RequestId = 2401,
            ChatIsChannel = false,
            BotIsMember = true,
            RequestTitle = true
        };
        await botClient.SendMessage(
            telegramUserId,
            "Выберите Telegram-группу для кэмпа. Telegram передаст ID и название; после этого бот отдельно проверит доступ к группе.",
            replyMarkup: new ReplyKeyboardMarkup([[new KeyboardButton("Выбрать группу") { RequestChat = requestChat }]])
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            },
            cancellationToken: cancellationToken);
    }

    private async Task SetStateAsync(
        long participantId,
        string stateName,
        string? dataJson,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.ParticipantConversationStates.SingleOrDefaultAsync(
            value => value.ParticipantId == participantId,
            cancellationToken);
        if (state is null)
        {
            state = new ParticipantConversationState { ParticipantId = participantId };
            dbContext.ParticipantConversationStates.Add(state);
        }
        state.State = stateName;
        state.DataJson = dataJson;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        state.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ShowParticipantsAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        var query = CampRegistrationsQuery();
        var total = await query.CountAsync(cancellationToken);
        var participants = await query
            .OrderBy(value => value.Participant.PreferredDisplayName ?? value.Participant.DisplayName)
            .ThenBy(value => value.Id)
            .Take(50)
            .Select(value => new
            {
                value.DaysStaying,
                CampName = value.Camp.Name,
                value.Participant
            })
            .ToListAsync(cancellationToken);

        var lines = participants.Select(value =>
            $"• {ParticipantPresentation.ToHtmlLink(value.Participant)} — {value.CampName}, {value.DaysStaying} дн.");
        var listText = participants.Count == 0
            ? "Участников пока нет."
            : string.Join('\n', lines);
        var truncated = total > participants.Count
            ? $"\n\nПоказаны первые {participants.Count}. Полный список есть в CSV-экспорте."
            : string.Empty;

        var text = $"""
            👥 Участники

            Всего: {total}

            {listText}{truncated}
            """;

        await SendOrEditAsync(
            callbackQuery,
            chatId,
            text,
            BuildBackKeyboard(),
            cancellationToken,
            ParseMode.Html);
    }

    private async Task ShowAccommodationAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        var query = CampRegistrationsQuery()
            .Where(value => value.NeedsAccommodation == true);
        var participantCount = await query.CountAsync(cancellationToken);
        var personDays = await query.SumAsync(
            value => value.DaysStaying ?? 0,
            cancellationToken);
        var text = $"""
            🏠 Жильё

            Нужно жильё: {participantCount}
            Человеко-дней: {personDays}
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
        var clubs = await dbContext.Clubs.AsNoTracking().Select(value => value.CollectionJson).ToArrayAsync(cancellationToken);
        var camps = await dbContext.Camps.AsNoTracking().Select(value => value.BaseCollectionJson).ToArrayAsync(cancellationToken);
        var clubGames = clubs.Sum(value => ClubCollectionSerializer.Deserialize(value).Games.Count);
        var campBaseGames = camps.Sum(value => ClubCollectionSerializer.Deserialize(value).Games.Count);
        var contributions = await dbContext.CampGameContributions.AsNoTracking().CountAsync(cancellationToken);

        var text = $"""
            🎲 Игры

            • Игр в коллекциях клубов: {clubGames}
            • Игр в базовых снимках кэмпов: {campBaseGames}
            • Вкладов участников кэмпов: {contributions}
            """;

        await SendOrEditAsync(
            callbackQuery,
            chatId,
            text,
            BuildBackKeyboard(),
            cancellationToken);
    }

    private async Task ShowStatisticsAsync(
        CallbackQuery callbackQuery,
        long chatId,
        CancellationToken cancellationToken)
    {
        var registeredParticipants = await CampRegistrationsQuery().CountAsync(cancellationToken);
        var accommodationParticipants = await CampRegistrationsQuery()
            .CountAsync(value => value.NeedsAccommodation == true, cancellationToken);
        var clubs = await dbContext.Clubs.AsNoTracking().CountAsync(cancellationToken);
        var camps = await dbContext.Camps.AsNoTracking().CountAsync(cancellationToken);
        var gatherings = await dbContext.GameGatherings.AsNoTracking().CountAsync(cancellationToken);
        var recruitingGatherings = await dbContext.GameGatherings.AsNoTracking()
            .CountAsync(value => value.Status == GatheringStatus.Recruiting, cancellationToken);
        var readyGatherings = await dbContext.GameGatherings.AsNoTracking()
            .CountAsync(value => value.Status == GatheringStatus.Ready || value.Status == GatheringStatus.Full, cancellationToken);
        var completedGatherings = await dbContext.GameGatherings.AsNoTracking()
            .CountAsync(value => value.Status == GatheringStatus.Completed, cancellationToken);

        var text = $"""
            📊 Статистика

            Участники:
            • Зарегистрировано: {registeredParticipants}
            • Нужно жильё: {accommodationParticipants}

            Сообщества:
            • Клубов: {clubs}
            • Кэмпов: {camps}

            Сборы:
            • Всего: {gatherings}
            • Набор открыт: {recruitingGatherings}
            • Готовы или заполнены: {readyGatherings}
            • Завершены: {completedGatherings}
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
            "📤 Экспорт\n\nОтправляю четыре CSV-файла новой модели:\n\n• communities.csv\n• camp-registrations.csv\n• camp-contributions.csv\n• gatherings.csv",
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

    private IQueryable<CampRegistration> CampRegistrationsQuery() =>
        dbContext.CampRegistrations
            .AsNoTracking()
            .Where(value => value.DaysStaying.HasValue
                && value.DaysStaying.Value >= 1
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

    private static string BuildMenuText() => """
        🛠 Админ-панель

        👥 Участники
        Регистрации по кэмпам.

        🏠 Жильё
        Сводка по проживанию.

        🎲 Игры
        Коллекции клубов, снимки кэмпов и вклады участников.

        📊 Статистика
        Общие счётчики.

        📤 Экспорт
        Четыре CSV-файла новой модели в личный чат.
        """;

    private static InlineKeyboardMarkup BuildMenu() =>
        new(
        [
            [
                InlineKeyboardButton.WithCallbackData("👥 Участники", "admin:participants"),
                InlineKeyboardButton.WithCallbackData("🏠 Жильё", "admin:accommodation")
            ],
            [InlineKeyboardButton.WithCallbackData("🎲 Игры", "admin:games")],
            [InlineKeyboardButton.WithCallbackData("🏕 Создать кэмп", "admin:camp:create")],
            [
                InlineKeyboardButton.WithCallbackData("📊 Статистика", "admin:stats"),
                InlineKeyboardButton.WithCallbackData("📤 Экспорт", "admin:export")
            ]
        ]);

    private sealed record CampCreationState(string Name, long? SourceClubId);

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
        CancellationToken cancellationToken,
        ParseMode parseMode = default)
    {
        if (callbackQuery.Message is { } callbackMessage
            && callbackMessage.Chat.Id == chatId)
        {
            await botClient.EditMessageText(
                chatId,
                callbackMessage.MessageId,
                text,
                parseMode: parseMode,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(
            chatId,
            text,
            parseMode: parseMode,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }
}
