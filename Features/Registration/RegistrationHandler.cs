using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Features.Registration;

public sealed class RegistrationHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    IOptions<CampOptions> campOptions)
{
    private const string AwaitingAccommodationState = "registration:awaiting-accommodation";
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(30);

    public async Task HandleStartAsync(
        Participant participant,
        Message message,
        CancellationToken cancellationToken)
    {
        RefreshTelegramIdentity(participant, message.From);

        if (IsRegistrationComplete(participant))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await ShowMainMenuAsync(message.Chat.Id, cancellationToken);
            return;
        }

        await ClearConversationStateAsync(participant.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await botClient.SendMessage(
            message.Chat.Id,
            "На сколько дней вы едете?",
            replyMarkup: Keyboards.RegistrationDays,
            cancellationToken: cancellationToken);
    }

    public async Task HandleMenuAsync(
        Participant participant,
        Message message,
        CancellationToken cancellationToken)
    {
        RefreshTelegramIdentity(participant, message.From);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!IsRegistrationComplete(participant))
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "Сначала завершите регистрацию.",
                replyMarkup: Keyboards.RegistrationDays,
                cancellationToken: cancellationToken);
            return;
        }

        await ShowMainMenuAsync(message.Chat.Id, cancellationToken);
    }

    public async Task HandleProfileAsync(
        Participant participant,
        Message message,
        CancellationToken cancellationToken)
    {
        RefreshTelegramIdentity(participant, message.From);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!IsRegistrationComplete(participant))
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "Регистрация ещё не завершена.",
                replyMarkup: Keyboards.RegistrationDays,
                cancellationToken: cancellationToken);
            return;
        }

        await SendProfileAsync(message.Chat.Id, participant, cancellationToken);
    }

    public async Task<bool> HandleCallbackAsync(
        Participant participant,
        CallbackQuery callbackQuery,
        string callbackData,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null)
        {
            return false;
        }

        RefreshTelegramIdentity(participant, callbackQuery.From);

        if (callbackData.StartsWith("reg:days:", StringComparison.Ordinal))
        {
            if (!int.TryParse(callbackData["reg:days:".Length..], out var days)
                || days is < 1 or > 3)
            {
                return true;
            }

            await SetConversationStateAsync(
                participant.Id,
                AwaitingAccommodationState,
                JsonSerializer.Serialize(new RegistrationDraft(days)),
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            var price = campOptions.Value.AccommodationPricePerDay;
            await botClient.SendMessage(
                chatId.Value,
                $"Нужно жильё? Стоимость — {price:0} ₸ в день.",
                replyMarkup: Keyboards.Accommodation,
                cancellationToken: cancellationToken);
            return true;
        }

        if (callbackData.StartsWith("reg:accommodation:", StringComparison.Ordinal))
        {
            var state = await dbContext.ParticipantConversationStates
                .SingleOrDefaultAsync(x => x.ParticipantId == participant.Id, cancellationToken);

            var draft = state is null ? null : DeserializeDraft(state.DataJson);
            if (state is null
                || state.State != AwaitingAccommodationState
                || state.ExpiresAt <= DateTimeOffset.UtcNow
                || draft is null)
            {
                if (state is not null)
                {
                    dbContext.ParticipantConversationStates.Remove(state);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                await botClient.SendMessage(
                    chatId.Value,
                    "Регистрация устарела. Выберите количество дней ещё раз.",
                    replyMarkup: Keyboards.RegistrationDays,
                    cancellationToken: cancellationToken);
                return true;
            }

            bool? needsAccommodation = callbackData switch
            {
                "reg:accommodation:yes" => true,
                "reg:accommodation:no" => false,
                _ => null
            };

            if (!needsAccommodation.HasValue)
            {
                return true;
            }

            participant.DaysStaying = draft.DaysStaying;
            participant.NeedsAccommodation = needsAccommodation.Value;
            participant.UpdatedAt = DateTimeOffset.UtcNow;
            dbContext.ParticipantConversationStates.Remove(state);
            await dbContext.SaveChangesAsync(cancellationToken);

            await botClient.SendMessage(
                chatId.Value,
                "✅ Готово",
                replyMarkup: Keyboards.MainMenu,
                cancellationToken: cancellationToken);
            return true;
        }

        if (callbackData == "reg:edit")
        {
            await ClearConversationStateAsync(participant.Id, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await botClient.SendMessage(
                chatId.Value,
                "Изменение регистрации. На сколько дней вы едете?",
                replyMarkup: Keyboards.RegistrationDays,
                cancellationToken: cancellationToken);
            return true;
        }

        return false;
    }

    public Task<bool> HandleConversationTextAsync(
        Participant participant,
        ParticipantConversationState conversationState,
        Message message,
        CancellationToken cancellationToken) => Task.FromResult(false);

    private async Task SendProfileAsync(
        long chatId,
        Participant participant,
        CancellationToken cancellationToken)
    {
        var accommodation = participant.NeedsAccommodation == true ? "Да" : "Нет";
        var text = $"👤 Моё\n\nДней: {participant.DaysStaying}\nЖильё: {accommodation}";

        await botClient.SendMessage(
            chatId,
            text,
            replyMarkup: Keyboards.Profile,
            cancellationToken: cancellationToken);
    }

    private async Task ShowMainMenuAsync(long chatId, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(
            chatId,
            "Главное меню",
            replyMarkup: Keyboards.MainMenu,
            cancellationToken: cancellationToken);
    }

    private async Task SetConversationStateAsync(
        long participantId,
        string stateName,
        string? dataJson,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var state = await dbContext.ParticipantConversationStates
            .SingleOrDefaultAsync(x => x.ParticipantId == participantId, cancellationToken);

        if (state is null)
        {
            state = new ParticipantConversationState
            {
                ParticipantId = participantId
            };
            dbContext.ParticipantConversationStates.Add(state);
        }

        state.State = stateName;
        state.DataJson = dataJson;
        state.ExpiresAt = now.Add(StateTtl);
        state.UpdatedAt = now;
    }

    private async Task ClearConversationStateAsync(long participantId, CancellationToken cancellationToken)
    {
        var state = await dbContext.ParticipantConversationStates
            .SingleOrDefaultAsync(x => x.ParticipantId == participantId, cancellationToken);

        if (state is not null)
        {
            dbContext.ParticipantConversationStates.Remove(state);
        }
    }

    private static RegistrationDraft? DeserializeDraft(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RegistrationDraft>(dataJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRegistrationComplete(Participant participant) =>
        participant.DaysStaying is >= 1 and <= 3
        && participant.NeedsAccommodation.HasValue;

    private static void RefreshTelegramIdentity(Participant participant, User? user)
    {
        if (user is null)
        {
            return;
        }

        participant.TelegramUsername = user.Username;
        participant.DisplayName = BuildDisplayName(user);
        participant.UpdatedAt = DateTimeOffset.UtcNow;
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

    private sealed record RegistrationDraft(int DaysStaying);
}
