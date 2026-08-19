using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Features.Registration;

public sealed class RegistrationHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    IOptions<CampOptions> campOptions)
{
    private const string AwaitingAccommodationState = "registration:awaiting-accommodation";
    private const string AwaitingDisplayNameState = "registration:awaiting-display-name";
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
            await ShowMainMenuAsync(message.Chat.Id, participant.TelegramUserId, cancellationToken);
            return;
        }

        await ClearConversationStateAsync(participant.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await botClient.SendMessage(
            message.Chat.Id,
            "Регистрация займёт три коротких шага.\n\n1/3. На сколько дней вы едете?",
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
                "Сначала завершите регистрацию. Выберите количество дней.",
                replyMarkup: Keyboards.RegistrationDays,
                cancellationToken: cancellationToken);
            return;
        }

        await ShowMainMenuAsync(message.Chat.Id, participant.TelegramUserId, cancellationToken);
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
            await botClient.EditMessageText(
                chatId.Value,
                callbackQuery.Message!.Id,
                $"2/3. Нужно жильё? Стоимость — {price:0} ₸ в день.",
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

                await botClient.EditMessageText(
                    chatId.Value,
                    callbackQuery.Message!.Id,
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
            await SetConversationStateAsync(
                participant.Id,
                AwaitingDisplayNameState,
                null,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await botClient.EditMessageText(
                chatId.Value,
                callbackQuery.Message!.Id,
                "3/3. Как вас показывать другим участникам?\n\nОтправьте предпочтительное имя одним сообщением. Можно нажать «Пропустить» — тогда бот будет использовать имя из Telegram.",
                replyMarkup: Keyboards.DisplayName,
                cancellationToken: cancellationToken);
            return true;
        }

        if (callbackData == "reg:name:skip")
        {
            var state = await dbContext.ParticipantConversationStates
                .SingleOrDefaultAsync(x => x.ParticipantId == participant.Id, cancellationToken);
            if (state?.State != AwaitingDisplayNameState)
            {
                return true;
            }

            participant.PreferredDisplayName = null;
            participant.UpdatedAt = DateTimeOffset.UtcNow;
            dbContext.ParticipantConversationStates.Remove(state);
            await dbContext.SaveChangesAsync(cancellationToken);

            await botClient.EditMessageText(
                chatId.Value,
                callbackQuery.Message!.Id,
                $"✅ Готово. Буду показывать вас как {ParticipantPresentation.GetDisplayName(participant)}.",
                cancellationToken: cancellationToken);
            await ShowMainMenuAsync(chatId.Value, participant.TelegramUserId, cancellationToken);
            return true;
        }

        if (callbackData == "reg:edit")
        {
            await ClearConversationStateAsync(participant.Id, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await botClient.SendMessage(
                chatId.Value,
                "Изменение регистрации. После дней и жилья можно также обновить отображаемое имя.\n\n1/3. На сколько дней вы едете?",
                replyMarkup: Keyboards.RegistrationDays,
                cancellationToken: cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<bool> HandleConversationTextAsync(
        Participant participant,
        ParticipantConversationState conversationState,
        Message message,
        CancellationToken cancellationToken)
    {
        if (conversationState.State != AwaitingDisplayNameState)
        {
            return false;
        }

        if (message.Chat.Type != ChatType.Private)
        {
            return true;
        }

        var preferredName = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(preferredName))
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "Имя не может быть пустым. Отправьте имя или нажмите «Пропустить» в предыдущем сообщении.",
                cancellationToken: cancellationToken);
            return true;
        }

        if (preferredName.Length > 128)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "Слишком длинное имя. Максимум 128 символов.",
                cancellationToken: cancellationToken);
            return true;
        }

        participant.PreferredDisplayName = preferredName;
        participant.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.ParticipantConversationStates.Remove(conversationState);
        await dbContext.SaveChangesAsync(cancellationToken);

        await botClient.SendMessage(
            message.Chat.Id,
            $"✅ Готово. Буду показывать вас как {ParticipantPresentation.GetDisplayName(participant)}.",
            replyMarkup: Keyboards.MainMenuFor(IsAdmin(participant.TelegramUserId)),
            cancellationToken: cancellationToken);
        return true;
    }

    private async Task SendProfileAsync(
        long chatId,
        Participant participant,
        CancellationToken cancellationToken)
    {
        var accommodation = participant.NeedsAccommodation == true ? "Да" : "Нет";
        var name = ParticipantPresentation.GetDisplayName(participant);
        var text = $"👤 Моё\n\nИмя для участников: {name}\nДней: {participant.DaysStaying}\nЖильё: {accommodation}\n\nЗдесь можно изменить регистрацию, открыть свои игры или свои хотелки.";

        await botClient.SendMessage(
            chatId,
            text,
            replyMarkup: Keyboards.Profile,
            cancellationToken: cancellationToken);
    }

    private async Task ShowMainMenuAsync(
        long chatId,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var isAdmin = IsAdmin(telegramUserId);
        var text = """
            Главное меню

            🎲 Игры — каталог: спрос, подтверждённые привозы, возможные игры и коллекции участников.
            ➕ Добавить игры — добавить одну игру или импортировать личную коллекцию.
            🔥 Хочу сыграть — посмотреть спрос и управлять своими хотелками.
            ▶️ Собрать игру — создать новый набор игроков.
            🎲 Текущие сборы — открытые сейчас наборы, к которым можно присоединиться.
            👤 Моё — регистрация, мои игры и мои хотелки.
            """;

        if (isAdmin)
        {
            text += "\n🛠 Админ-панель — участники, игры, статистика, коллекция клуба и CSV-экспорт.";
        }

        await botClient.SendMessage(
            chatId,
            text,
            replyMarkup: Keyboards.MainMenuFor(isAdmin),
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

    private bool IsAdmin(long telegramUserId) =>
        campOptions.Value.AdminTelegramIds.Contains(telegramUserId);

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
        participant.DisplayName = BuildTelegramDisplayName(user);
        participant.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string BuildTelegramDisplayName(User user)
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
