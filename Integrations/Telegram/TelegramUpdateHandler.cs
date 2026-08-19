using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Games;
using oyinQ.Bot.Features.Interests;
using oyinQ.Bot.Features.Registration;
using oyinQ.Bot.Features.Sessions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramUpdateHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    RegistrationHandler registrationHandler,
    CollectionsHandler collectionsHandler,
    GamesHandler gamesHandler,
    InterestsHandler interestsHandler,
    SessionsHandler sessionsHandler,
    AdminHandler adminHandler,
    IOptions<CampOptions> campOptions,
    IOptions<BggOptions> bggOptions,
    ILogger<TelegramUpdateHandler> logger)
{
    public async Task HandleAsync(Update update, CancellationToken cancellationToken)
    {
        var callbackQuery = update.CallbackQuery;
        if (callbackQuery is not null && !CallbackDataValidator.IsValid(callbackQuery.Data))
        {
            await botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "Эта кнопка устарела или повреждена.",
                showAlert: true,
                cancellationToken: cancellationToken);
            logger.LogWarning(
                "Ignoring malformed Telegram callback payload for update {UpdateId}.",
                update.Id);
            return;
        }

        var telegramUser = callbackQuery?.From ?? update.Message?.From;
        if (telegramUser is null)
        {
            return;
        }

        var command = GetCommand(update.Message?.Text);
        if (update.Message is { } incomingMessage
            && incomingMessage.Chat.Type != ChatType.Private
            && IsInteractiveGroupMessage(incomingMessage.Text, command))
        {
            await SendPrivateEntryPointAsync(incomingMessage.Chat.Id, cancellationToken);
            return;
        }

        if (callbackQuery is { } groupCallback
            && groupCallback.Message?.Chat.Type != ChatType.Private
            && !IsGroupSessionParticipation(groupCallback.Data))
        {
            await botClient.AnswerCallbackQuery(
                groupCallback.Id,
                "Это действие выполняется в личном чате с ботом.",
                showAlert: true,
                cancellationToken: cancellationToken);
            return;
        }

        if (command == "/admin" && update.Message is { } adminMessage)
        {
            await adminHandler.HandleCommandAsync(
                adminMessage,
                telegramUser.Id,
                cancellationToken);
            return;
        }

        if (callbackQuery is { Data: { } adminCallbackData } adminCallback
            && adminCallbackData.StartsWith("admin:", StringComparison.Ordinal))
        {
            if (!IsAdmin(telegramUser.Id))
            {
                await botClient.AnswerCallbackQuery(
                    adminCallback.Id,
                    "Доступ запрещён.",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return;
            }

            await botClient.AnswerCallbackQuery(
                adminCallback.Id,
                cancellationToken: cancellationToken);
            await adminHandler.TryHandleCallbackAsync(
                adminCallback,
                telegramUser.Id,
                cancellationToken);
            return;
        }

        var participant = await dbContext.Participants
            .SingleOrDefaultAsync(
                value => value.TelegramUserId == telegramUser.Id,
                cancellationToken);

        if (participant is null && command == "/start")
        {
            var now = DateTimeOffset.UtcNow;
            participant = new Participant
            {
                TelegramUserId = telegramUser.Id,
                TelegramUsername = telegramUser.Username,
                DisplayName = BuildDisplayName(telegramUser),
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.Participants.Add(participant);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (participant is null)
        {
            if (callbackQuery is not null)
            {
                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "Сначала откройте бота в личном чате и зарегистрируйтесь через /start.",
                    showAlert: true,
                    cancellationToken: cancellationToken);
            }
            else
            {
                logger.LogDebug(
                    "Ignoring Telegram update {UpdateId} from unregistered user {TelegramUserId}.",
                    update.Id,
                    telegramUser.Id);
            }

            return;
        }

        var conversationState = await dbContext.ParticipantConversationStates
            .SingleOrDefaultAsync(
                value => value.ParticipantId == participant.Id,
                cancellationToken);

        if (conversationState is not null
            && conversationState.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            dbContext.ParticipantConversationStates.Remove(conversationState);
            await dbContext.SaveChangesAsync(cancellationToken);
            conversationState = null;
        }

        if (update.Message is { Chat.Type: not ChatType.Private } groupMessage
            && conversationState is not null)
        {
            await SendPrivateEntryPointAsync(groupMessage.Chat.Id, cancellationToken);
            return;
        }

        if (command is not null && update.Message is { } commandMessage)
        {
            switch (command)
            {
                case "/start":
                    await registrationHandler.HandleStartAsync(
                        participant,
                        commandMessage,
                        cancellationToken);
                    return;

                case "/menu":
                    await registrationHandler.HandleMenuAsync(
                        participant,
                        commandMessage,
                        cancellationToken);
                    return;
            }

            if (IsGameCommand(command) && !IsRegistrationComplete(participant))
            {
                await SendRegistrationRequiredAsync(commandMessage.Chat.Id, cancellationToken);
                return;
            }

            if (await gamesHandler.TryHandleMessageAsync(
                    commandMessage,
                    telegramUser.Id,
                    conversationState,
                    cancellationToken))
            {
                return;
            }

            logger.LogDebug("Ignoring unknown command {Command}.", command);
            return;
        }

        if (callbackQuery is { Data: { } callbackData } callback)
        {
            if (IsGameCallback(callbackData) && !IsRegistrationComplete(participant))
            {
                await botClient.AnswerCallbackQuery(
                    callback.Id,
                    "Сначала завершите регистрацию в личном чате с ботом.",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return;
            }

            if (IsClubImportCallback(callbackData) && !IsAdmin(telegramUser.Id))
            {
                await botClient.AnswerCallbackQuery(
                    callback.Id,
                    "Импорт коллекции клуба доступен только администратору.",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return;
            }

            if (IsUnavailableBggCallback(callbackData))
            {
                await botClient.AnswerCallbackQuery(
                    callback.Id,
                    "BGG API пока недоступен. Используйте Tesera или повторите после включения BGG.",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return;
            }

            if (callbackData.StartsWith("session:", StringComparison.Ordinal))
            {
                await sessionsHandler.TryHandleCallbackAsync(
                    callback,
                    telegramUser.Id,
                    cancellationToken);
                return;
            }

            await botClient.AnswerCallbackQuery(
                callback.Id,
                cancellationToken: cancellationToken);

            if (callbackData.StartsWith("collection:", StringComparison.Ordinal)
                && await collectionsHandler.TryHandleCallbackAsync(
                    callback,
                    telegramUser.Id,
                    cancellationToken))
            {
                return;
            }

            if (callbackData.StartsWith("interest:", StringComparison.Ordinal)
                && await interestsHandler.TryHandleCallbackAsync(
                    callback,
                    telegramUser.Id,
                    cancellationToken))
            {
                return;
            }

            if ((callbackData.StartsWith("game:", StringComparison.Ordinal)
                    || callbackData.StartsWith("copy:", StringComparison.Ordinal))
                && await gamesHandler.TryHandleCallbackAsync(
                    callback,
                    telegramUser.Id,
                    cancellationToken))
            {
                return;
            }

            if (callbackData.StartsWith("reg:", StringComparison.Ordinal))
            {
                await registrationHandler.HandleCallbackAsync(
                    participant,
                    callback,
                    callbackData,
                    cancellationToken);
                return;
            }

            logger.LogDebug("Ignoring unknown callback payload.");
            return;
        }

        if (update.Message is { Text: { } text } message)
        {
            if (text == "👤 Моё")
            {
                await registrationHandler.HandleProfileAsync(
                    participant,
                    message,
                    cancellationToken);
                return;
            }

            if (IsGameMenuText(text) && !IsRegistrationComplete(participant))
            {
                await SendRegistrationRequiredAsync(message.Chat.Id, cancellationToken);
                return;
            }

            if (await collectionsHandler.TryHandleMessageAsync(
                    message,
                    telegramUser.Id,
                    conversationState,
                    cancellationToken))
            {
                return;
            }

            if (await sessionsHandler.TryHandleMessageAsync(
                    message,
                    telegramUser.Id,
                    conversationState,
                    cancellationToken))
            {
                return;
            }

            if (await gamesHandler.TryHandleMessageAsync(
                    message,
                    telegramUser.Id,
                    conversationState,
                    cancellationToken))
            {
                return;
            }

            if (conversationState is not null)
            {
                var handled = await registrationHandler.HandleConversationTextAsync(
                    participant,
                    conversationState,
                    message,
                    cancellationToken);
                if (handled)
                {
                    return;
                }
            }
        }
    }

    private async Task SendRegistrationRequiredAsync(
        long chatId,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessage(
            chatId,
            "Сначала завершите регистрацию.",
            replyMarkup: Keyboards.RegistrationDays,
            cancellationToken: cancellationToken);
    }

    private async Task SendPrivateEntryPointAsync(
        long groupChatId,
        CancellationToken cancellationToken)
    {
        try
        {
            var me = await botClient.GetMe(cancellationToken);
            if (!string.IsNullOrWhiteSpace(me.Username))
            {
                var url = $"https://t.me/{me.Username}?start=menu";
                await botClient.SendMessage(
                    groupChatId,
                    "Меню, поиск, импорт и управление выполняются в личном чате. В группе остаются сообщения сборов и точки входа.",
                    replyMarkup: new InlineKeyboardMarkup([
                        [InlineKeyboardButton.WithUrl("Открыть бота", url)]
                    ]),
                    cancellationToken: cancellationToken);
                return;
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(exception, "Could not build private bot deep link.");
        }

        await botClient.SendMessage(
            groupChatId,
            "Откройте личный чат с ботом, чтобы продолжить.",
            cancellationToken: cancellationToken);
    }

    private bool IsAdmin(long telegramUserId) =>
        campOptions.Value.AdminTelegramIds.Contains(telegramUserId);

    private bool IsUnavailableBggCallback(string callbackData) =>
        !bggOptions.Value.IsAvailable
        && (callbackData.StartsWith("collection:import:bgg:", StringComparison.Ordinal)
            || callbackData.StartsWith("game:add:", StringComparison.Ordinal));

    private static bool IsRegistrationComplete(Participant participant) =>
        participant.DaysStaying is >= 1 and <= 3
        && participant.NeedsAccommodation.HasValue;

    private static bool IsClubImportCallback(string? callbackData) =>
        callbackData?.StartsWith("collection:import:", StringComparison.Ordinal) == true
        && callbackData.EndsWith(":club", StringComparison.Ordinal);

    private static bool IsGameCallback(string? callbackData) =>
        callbackData?.StartsWith("game:", StringComparison.Ordinal) == true
        || callbackData?.StartsWith("interest:", StringComparison.Ordinal) == true
        || callbackData?.StartsWith("copy:", StringComparison.Ordinal) == true
        || callbackData?.StartsWith("collection:", StringComparison.Ordinal) == true
        || callbackData?.StartsWith("session:", StringComparison.Ordinal) == true;

    private static bool IsGroupSessionParticipation(string? callbackData) =>
        callbackData?.StartsWith("session:join:", StringComparison.Ordinal) == true
        || callbackData?.StartsWith("session:leave:", StringComparison.Ordinal) == true;

    private static bool IsGameCommand(string command) =>
        command is "/games" or "/addgame" or "/wanted" or "/mygames";

    private static bool IsGameMenuText(string text) =>
        text is "🎲 Игры"
            or "➕ Добавить игры"
            or "🔥 Хочу сыграть"
            or "▶️ Собрать игру"
            or "🎲 Текущие сборы"
            or "Мои игры"
            or "🎲 Мои игры"
            or "Мои хотелки"
            or "🔥 Мои хотелки";

    private static bool IsInteractiveGroupMessage(string? text, string? command)
    {
        if (command is "/start" or "/menu" or "/admin" or "/games" or "/addgame" or "/wanted" or "/mygames")
        {
            return true;
        }

        return text is not null && (IsGameMenuText(text) || text == "👤 Моё");
    }

    private static string? GetCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '/')
        {
            return null;
        }

        var token = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return token.Split('@', 2)[0].ToLowerInvariant();
    }

    private static string BuildDisplayName(User user)
    {
        var name = string.Join(
            ' ',
            new[] { user.FirstName, user.LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(name)
            ? user.Username ?? user.Id.ToString()
            : name;
    }
}
