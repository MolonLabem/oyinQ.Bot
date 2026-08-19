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
    private const string RegistrationIntro = """
        🍂 Осенний Астанинский Настолкомарафон-2026

        📍 Клуб «Кинь-Двинь»
        🗓 26 сентября 2026, 09:00 — 29 сентября 2026, 09:00
        🎲 Три дня настольных игр нон-стоп

        💳 Участие:
        • 1 день — 10 000 ₸
        • 3 дня — 15 000 ₸

        🏠 Проживание:
        • квартира рядом с клубом
        • 3 000 ₸ за сутки с человека

        ❓ Вопросы: @andreyjugg

        Регистрация — 3 коротких шага.

        1/3. На сколько дней вы едете?
        """;
    private const string PaymentDetails = """
        💳 Оплата участия

        Бот не принимает платежи — здесь только реквизиты организатора.

        Kaspi: +7 747 120 8577
        Получатель: Andrei K.

        После перевода отправьте чек в ЛС @andreyjugg.

        Если оплачиваете проживание, укажите количество дней.
        """;

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
            RegistrationIntro,
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
                "Сначала завершите регистрацию.\n\n1/3. На сколько дней вы едете?",
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

        if (callbackData == "reg:payment")
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await botClient.EditMessageText(
                chatId.Value,
                callbackQuery.Message!.Id,
                PaymentDetails,
                replyMarkup: Keyboards.Payment,
                cancellationToken: cancellationToken);
            return true;
        }

        if (callbackData == "reg:profile")
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await botClient.EditMessageText(
                chatId.Value,
                callbackQuery.Message!.Id,
                BuildProfileText(participant),
                replyMarkup: Keyboards.Profile,
                cancellationToken: cancellationToken);
            return true;
        }

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
                $"2/3. Нужно жильё?\n\nСтоимость — {price:0} ₸ за сутки с человека.",
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
                    "Регистрация устарела.\n\nВыберите количество дней ещё раз.",
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
                "3/3. Как вас показывать другим участникам?\n\nОтправьте предпочтительное имя одним сообщением.\n\nМожно нажать «Пропустить» — тогда бот будет использовать имя из Telegram.",
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
                $"✅ Готово.\n\nДля других участников вы будете отображаться как {ParticipantPresentation.GetDisplayName(participant)}.",
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
                "Изменение регистрации\n\nПосле дней и жилья можно также обновить отображаемое имя.\n\n1/3. На сколько дней вы едете?",
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
                "Имя не может быть пустым.\n\nОтправьте имя или нажмите «Пропустить» в предыдущем сообщении.",
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
            $"✅ Готово.\n\nДля других участников вы будете отображаться как {ParticipantPresentation.GetDisplayName(participant)}.",
            replyMarkup: Keyboards.MainMenuFor(IsAdmin(participant.TelegramUserId)),
            cancellationToken: cancellationToken);
        return true;
    }

    private async Task SendProfileAsync(
        long chatId,
        Participant participant,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessage(
            chatId,
            BuildProfileText(participant),
            replyMarkup: Keyboards.Profile,
            cancellationToken: cancellationToken);
    }

    private static string BuildProfileText(Participant participant)
    {
        var accommodation = participant.NeedsAccommodation == true ? "Да" : "Нет";
        var name = ParticipantPresentation.GetDisplayName(participant);
        return $"""
            👤 Моё

            Имя для участников: {name}
            Дней: {participant.DaysStaying}
            Жильё: {accommodation}

            Здесь можно посмотреть реквизиты для оплаты, изменить регистрацию, открыть свои игры или свои хотелки.
            """;
    }

    private async Task ShowMainMenuAsync(
        long chatId,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var isAdmin = IsAdmin(telegramUserId);
        var text = """
            Главное меню

            🎲 Игры
            Каталог, спрос и доступность игр на Настолкомарафоне-2026.

            ➕ Добавить игры
            Добавить одну игру или импортировать личную коллекцию.

            🔥 Хочу сыграть
            Посмотреть общий спрос и управлять своими хотелками.

            ▶️ Собрать игру
            Создать новый набор игроков.

            🎲 Текущие сборы
            Посмотреть открытые наборы и присоединиться.

            👤 Моё
            Регистрация, мои игры и мои хотелки.
            """;

        if (isAdmin)
        {
            text += "\n\n🛠 Админ-панель\nУчастники, игры, статистика, коллекция клуба и CSV-экспорт.";
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
