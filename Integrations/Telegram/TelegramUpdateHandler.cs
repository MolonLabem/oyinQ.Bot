using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Games;
using oyinQ.Bot.Features.Interests;
using oyinQ.Bot.Features.Registration;
using oyinQ.Bot.Features.Sessions;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramUpdateHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    RegistrationHandler registrationHandler,
    CollectionsHandler collectionsHandler,
    GamesHandler gamesHandler,
    InterestsHandler interestsHandler,
    SessionsHandler sessionsHandler,
    ILogger<TelegramUpdateHandler> logger)
{
    private static readonly string[] DeferredCallbackPrefixes =
        ["admin:"];

    public async Task HandleAsync(Update update, CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is { } callbackQuery)
        {
            await botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                cancellationToken: cancellationToken);
        }

        var telegramUser = update.CallbackQuery?.From ?? update.Message?.From;
        if (telegramUser is null)
        {
            return;
        }

        var command = GetCommand(update.Message?.Text);
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
            logger.LogDebug(
                "Ignoring Telegram update {UpdateId} from unregistered user {TelegramUserId}.",
                update.Id,
                telegramUser.Id);
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

        if (update.CallbackQuery is { } callback)
        {
            if (IsGameCallback(callback.Data) && !IsRegistrationComplete(participant))
            {
                var chatId = callback.Data?.StartsWith("session:", StringComparison.Ordinal) == true
                    ? telegramUser.Id
                    : callback.Message?.Chat.Id ?? telegramUser.Id;
                await SendRegistrationRequiredAsync(chatId, cancellationToken);
                return;
            }

            if (callback.Data?.StartsWith("collection:", StringComparison.Ordinal) == true
                && await collectionsHandler.TryHandleCallbackAsync(
                    callback,
                    telegramUser.Id,
                    cancellationToken))
            {
                return;
            }

            if (callback.Data?.StartsWith("interest:", StringComparison.Ordinal) == true
                && await interestsHandler.TryHandleCallbackAsync(
                    callback,
                    telegramUser.Id,
                    cancellationToken))
            {
                return;
            }

            if (callback.Data?.StartsWith("session:", StringComparison.Ordinal) == true
                && await sessionsHandler.TryHandleCallbackAsync(
                    callback,
                    telegramUser.Id,
                    cancellationToken))
            {
                return;
            }

            if ((callback.Data?.StartsWith("game:", StringComparison.Ordinal) == true
                    || callback.Data?.StartsWith("copy:", StringComparison.Ordinal) == true)
                && await gamesHandler.TryHandleCallbackAsync(
                    callback,
                    telegramUser.Id,
                    cancellationToken))
            {
                return;
            }

            if (callback.Data?.StartsWith("reg:", StringComparison.Ordinal) == true)
            {
                await registrationHandler.HandleCallbackAsync(
                    participant,
                    callback,
                    callback.Data,
                    cancellationToken);
                return;
            }

            var prefix = DeferredCallbackPrefixes.FirstOrDefault(value =>
                callback.Data?.StartsWith(value, StringComparison.Ordinal) == true);
            logger.LogDebug(
                prefix is null
                    ? "Ignoring unknown callback payload."
                    : "Callback prefix {Prefix} is awaiting a later feature handler.",
                prefix);
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

    private static bool IsRegistrationComplete(Participant participant) =>
        participant.DaysStaying is >= 1 and <= 3
        && participant.NeedsAccommodation.HasValue;

    private static bool IsGameCallback(string? callbackData) =>
        callbackData?.StartsWith("game:", StringComparison.Ordinal) == true
        || callbackData?.StartsWith("interest:", StringComparison.Ordinal) == true
        || callbackData?.StartsWith("copy:", StringComparison.Ordinal) == true
        || callbackData?.StartsWith("collection:", StringComparison.Ordinal) == true
        || callbackData?.StartsWith("session:", StringComparison.Ordinal) == true;

    private static bool IsGameCommand(string command) =>
        command is "/games" or "/addgame" or "/wanted" or "/mygames";

    private static bool IsGameMenuText(string text) =>
        text is "🎲 Игры"
            or "➕ Добавить игры"
            or "🔥 Хочу сыграть"
            or "▶️ Собрать игру"
            or "Мои игры"
            or "🎲 Мои игры"
            or "Мои хотелки"
            or "🔥 Мои хотелки";

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
