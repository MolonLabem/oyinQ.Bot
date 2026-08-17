using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Registration;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramUpdateHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    RegistrationHandler registrationHandler,
    ILogger<TelegramUpdateHandler> logger)
{
    private static readonly string[] CallbackPrefixes =
        ["game:", "interest:", "copy:", "session:", "reg:", "admin:"];

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
                x => x.TelegramUserId == telegramUser.Id,
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
                x => x.ParticipantId == participant.Id,
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
                    await registrationHandler.HandleStartAsync(participant, commandMessage, cancellationToken);
                    return;
                case "/menu":
                    await registrationHandler.HandleMenuAsync(participant, commandMessage, cancellationToken);
                    return;
                default:
                    logger.LogDebug("Ignoring unknown command {Command}.", command);
                    return;
            }
        }

        if (update.CallbackQuery?.Data is { } callbackData)
        {
            if (callbackData.StartsWith("reg:", StringComparison.Ordinal))
            {
                await registrationHandler.HandleCallbackAsync(
                    participant,
                    update.CallbackQuery,
                    callbackData,
                    cancellationToken);
                return;
            }

            var prefix = CallbackPrefixes.FirstOrDefault(
                value => callbackData.StartsWith(value, StringComparison.Ordinal));

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
                await registrationHandler.HandleProfileAsync(participant, message, cancellationToken);
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

            if (text is "🎲 Игры" or "➕ Добавить игры" or "🔥 Хочу сыграть" or "▶️ Собрать игру")
            {
                await botClient.SendMessage(
                    message.Chat.Id,
                    "Этот раздел появится позже.",
                    replyMarkup: Keyboards.MainMenu,
                    cancellationToken: cancellationToken);
            }
        }
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
