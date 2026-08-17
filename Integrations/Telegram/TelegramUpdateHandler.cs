using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Games;
using oyinQ.Bot.Features.Interests;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramUpdateHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    GamesHandler gamesHandler,
    InterestsHandler interestsHandler,
    ILogger<TelegramUpdateHandler> logger)
{
    private static readonly string[] DeferredCallbackPrefixes =
        ["session:", "reg:", "admin:"];

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

        if (update.CallbackQuery is { } callback)
        {
            if (callback.Data?.StartsWith("interest:", StringComparison.Ordinal) == true
                && await interestsHandler.TryHandleCallbackAsync(
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

            var prefix = DeferredCallbackPrefixes.FirstOrDefault(value =>
                callback.Data?.StartsWith(value, StringComparison.Ordinal) == true);
            logger.LogDebug(
                prefix is null
                    ? "Ignoring unknown callback payload."
                    : "Callback prefix {Prefix} is awaiting a later feature handler.",
                prefix);
            return;
        }

        if (update.Message is { } message
            && await gamesHandler.TryHandleMessageAsync(
                message,
                telegramUser.Id,
                conversationState,
                cancellationToken))
        {
            return;
        }

        if (command is not null)
        {
            logger.LogDebug(
                "Command {Command} is awaiting another feature handler.",
                command);
            return;
        }

        if (update.Message?.Text is not null && conversationState is not null)
        {
            logger.LogDebug(
                "Conversation state {State} is awaiting another feature handler.",
                conversationState.State);
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
