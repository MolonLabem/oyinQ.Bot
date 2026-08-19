using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Integrations.Telegram;
using oyinQ.Bot.Integrations.Tesera;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Collections;

public sealed class CollectionsHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    CollectionImportService importService,
    IOptions<CampOptions> campOptions,
    IOptions<BggOptions> bggOptions)
{
    private const string GameAddSearchState = "games:add-search";
    private const string ImportStatePrefix = "collections:import:";
    private const string BggUnavailableMessage = "BGG пока недоступен — ждём подтверждение API-доступа. Tesera работает независимо.";
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(30);

    public async Task<bool> TryHandleMessageAsync(
        Message message,
        long telegramUserId,
        ParticipantConversationState? conversationState,
        CancellationToken cancellationToken)
    {
        if (message.Text is not { } rawText)
        {
            return false;
        }

        var text = rawText.Trim();
        if (conversationState?.State.StartsWith(ImportStatePrefix, StringComparison.Ordinal) == true)
        {
            if (message.Chat.Type != ChatType.Private)
            {
                return true;
            }

            if (text.Equals("Отмена", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Назад", StringComparison.OrdinalIgnoreCase))
            {
                await ClearStateAsync(conversationState, cancellationToken);
                await ShowAddMenuAsync(message.Chat.Id, telegramUserId, cancellationToken);
                return true;
            }

            await HandleImportInputAsync(
                message.Chat.Id,
                telegramUserId,
                conversationState,
                text,
                cancellationToken);
            return true;
        }

        if (text != "➕ Добавить игры")
        {
            return false;
        }

        if (message.Chat.Type != ChatType.Private)
        {
            return true;
        }

        await ShowAddMenuAsync(message.Chat.Id, telegramUserId, cancellationToken);
        return true;
    }

    public async Task<bool> TryHandleCallbackAsync(
        CallbackQuery callbackQuery,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrWhiteSpace(data)
            || !data.StartsWith("collection:", StringComparison.Ordinal))
        {
            return false;
        }

        if (callbackQuery.Message?.Chat.Type != ChatType.Private)
        {
            await botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "Эта операция доступна только в личном чате с ботом.",
                showAlert: true,
                cancellationToken: cancellationToken);
            return true;
        }

        var chatId = callbackQuery.Message.Chat.Id;
        if (data is "collection:menu" or "collection:cancel")
        {
            await ClearStateByUserAsync(telegramUserId, cancellationToken);
            await ShowAddMenuAsync(callbackQuery, chatId, telegramUserId, cancellationToken);
            return true;
        }

        if (data == "collection:add:single")
        {
            await SetStateAsync(telegramUserId, GameAddSearchState, cancellationToken);
            await SendOrEditAsync(
                callbackQuery,
                chatId,
                BuildSingleGamePrompt(),
                new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData("← Назад", "collection:menu")]
                ]),
                cancellationToken);
            return true;
        }

        var parts = data.Split(':');
        if (parts is ["collection", "import", var providerText, var targetText]
            && TryParseProvider(providerText, out var provider)
            && TryParseTarget(targetText, out var target))
        {
            if (target == ImportTarget.Club && !IsAdmin(telegramUserId))
            {
                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "Импорт коллекции клуба доступен только администратору.",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return true;
            }

            if (provider == ExternalGameProvider.Bgg && !bggOptions.Value.IsAvailable)
            {
                await botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    BggUnavailableMessage,
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return true;
            }

            await SetStateAsync(
                telegramUserId,
                BuildImportState(provider, target),
                cancellationToken);

            var prompt = provider == ExternalGameProvider.Bgg
                ? "Импорт BGG\n\nОтправьте имя пользователя BGG или ссылку на его профиль/коллекцию. Импорт работает в фоне и не меняет ваши статусы уже добавленных игр."
                : "Импорт Tesera\n\nОтправьте alias пользователя Tesera или ссылку на его профиль. Импорт работает в фоне; сбой Tesera не влияет на BGG или остальные функции бота.";
            await SendOrEditAsync(
                callbackQuery,
                chatId,
                prompt,
                ImportInputKeyboard(),
                cancellationToken);
            return true;
        }

        return true;
    }

    private async Task HandleImportInputAsync(
        long chatId,
        long telegramUserId,
        ParticipantConversationState conversationState,
        string input,
        CancellationToken cancellationToken)
    {
        var parts = conversationState.State.Split(':');
        if (parts is not ["collections", "import", _, _]
            || !TryParseProvider(parts[2], out var provider)
            || !TryParseTarget(parts[3], out var target))
        {
            await ClearStateAsync(conversationState, cancellationToken);
            return;
        }

        if (target == ImportTarget.Club && !IsAdmin(telegramUserId))
        {
            await ClearStateAsync(conversationState, cancellationToken);
            await botClient.SendMessage(
                chatId,
                "Импорт коллекции клуба доступен только администратору.",
                replyMarkup: BuildAddMenu(telegramUserId),
                cancellationToken: cancellationToken);
            return;
        }

        if (provider == ExternalGameProvider.Bgg && !bggOptions.Value.IsAvailable)
        {
            await ClearStateAsync(conversationState, cancellationToken);
            await botClient.SendMessage(
                chatId,
                BggUnavailableMessage,
                replyMarkup: BuildAddMenu(telegramUserId),
                cancellationToken: cancellationToken);
            return;
        }

        var externalUsername = provider == ExternalGameProvider.Bgg
            ? BggUsernameParser.Parse(input)
            : TeseraAliasParser.Parse(input);
        if (string.IsNullOrWhiteSpace(externalUsername))
        {
            await botClient.SendMessage(
                chatId,
                provider == ExternalGameProvider.Bgg
                    ? "Не удалось распознать пользователя BGG. Пришлите username или ссылку на профиль/коллекцию."
                    : "Не удалось распознать пользователя Tesera. Пришлите alias или ссылку на профиль.",
                replyMarkup: ImportInputKeyboard(),
                cancellationToken: cancellationToken);
            return;
        }

        CollectionImportEnqueueResult result;
        try
        {
            result = target == ImportTarget.Club
                ? await importService.EnqueueClubAsync(
                    telegramUserId,
                    provider,
                    externalUsername,
                    cancellationToken)
                : await importService.EnqueuePersonalAsync(
                    telegramUserId,
                    provider,
                    externalUsername,
                    cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            await ClearStateAsync(conversationState, cancellationToken);
            await botClient.SendMessage(
                chatId,
                "Импорт коллекции клуба доступен только администратору.",
                replyMarkup: BuildAddMenu(telegramUserId),
                cancellationToken: cancellationToken);
            return;
        }

        await ClearStateAsync(conversationState, cancellationToken);

        var responseMessage = result.Status switch
        {
            CollectionImportEnqueueStatus.Queued
                => "⏳ Импорт поставлен в очередь. Я напишу в личный чат после завершения.",
            CollectionImportEnqueueStatus.AlreadyQueued
                => "⏳ Такой импорт уже выполняется. Я напишу после завершения.",
            CollectionImportEnqueueStatus.RecentlyCompleted
                => "Эта коллекция уже импортировалась за последние 2 дня. Повторный импорт пока пропущен.",
            CollectionImportEnqueueStatus.Unavailable
                => BggUnavailableMessage,
            _ => "Импорт не поставлен в очередь."
        };

        await botClient.SendMessage(
            chatId,
            responseMessage,
            replyMarkup: Keyboards.MainMenuFor(IsAdmin(telegramUserId)),
            cancellationToken: cancellationToken);
    }

    private async Task ShowAddMenuAsync(
        long chatId,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessage(
            chatId,
            BuildAddMenuText(),
            replyMarkup: BuildAddMenu(telegramUserId),
            cancellationToken: cancellationToken);
    }

    private async Task ShowAddMenuAsync(
        CallbackQuery callbackQuery,
        long chatId,
        long telegramUserId,
        CancellationToken cancellationToken) =>
        await SendOrEditAsync(
            callbackQuery,
            chatId,
            BuildAddMenuText(),
            BuildAddMenu(telegramUserId),
            cancellationToken);

    private string BuildAddMenuText()
    {
        var bggState = bggOptions.Value.IsAvailable
            ? "BGG доступен."
            : "BGG API пока недоступен: ссылку BGG можно распознать, но новые метаданные без API не загружаются.";

        return $"""
            ➕ Добавить игры

            Одна игра — добавление по внешней ссылке, независимо от импорта коллекции.
            Личная коллекция — массовый импорт ваших игр; импортированные личные игры получают статус «Возможно».
            Коллекция клуба — отдельный админский импорт; обычным участникам он недоступен.

            {bggState}
            Tesera работает независимо от BGG.
            """;
    }

    private string BuildSingleGamePrompt() => bggOptions.Value.IsAvailable
        ? "Одна игра\n\nОтправьте ссылку Tesera вида tesera.ru/game/... или ссылку BGG вида boardgamegeek.com/boardgame/12345. Можно также отправить название — поиск по названию использует BGG."
        : "Одна игра\n\nОтправьте ссылку Tesera вида tesera.ru/game/... — её можно добавить независимо от BGG.\n\nСсылку BGG вида boardgamegeek.com/boardgame/12345 бот распознает по ID. Если игра уже есть в каталоге, её можно добавить себе. Для новой игры BGG метаданные требуют API-доступа; без него бот ничего не выдумывает.";

    private InlineKeyboardMarkup BuildAddMenu(long telegramUserId)
    {
        var rows = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("➕ Одна игра по ссылке", "collection:add:single")
            }
        };

        var personalImportButtons = new List<InlineKeyboardButton>();
        if (bggOptions.Value.IsAvailable)
        {
            personalImportButtons.Add(
                InlineKeyboardButton.WithCallbackData("Коллекция BGG", "collection:import:bgg:personal"));
        }

        personalImportButtons.Add(
            InlineKeyboardButton.WithCallbackData("Коллекция Tesera", "collection:import:tesera:personal"));
        rows.Add(personalImportButtons.ToArray());

        if (IsAdmin(telegramUserId))
        {
            var clubButtons = new List<InlineKeyboardButton>();
            if (bggOptions.Value.IsAvailable)
            {
                clubButtons.Add(
                    InlineKeyboardButton.WithCallbackData("🏢 BGG клуба", "collection:import:bgg:club"));
            }

            clubButtons.Add(
                InlineKeyboardButton.WithCallbackData("🏢 Tesera клуба", "collection:import:tesera:club"));
            rows.Add(clubButtons.ToArray());
        }

        return new InlineKeyboardMarkup(rows);
    }

    private static InlineKeyboardMarkup ImportInputKeyboard() =>
        new([
            [InlineKeyboardButton.WithCallbackData("← Назад", "collection:menu")],
            [InlineKeyboardButton.WithCallbackData("Отмена", "collection:cancel")]
        ]);

    private async Task SetStateAsync(
        long telegramUserId,
        string state,
        CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);
        var conversationState = await dbContext.ParticipantConversationStates.SingleOrDefaultAsync(
            value => value.ParticipantId == participant.Id,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (conversationState is null)
        {
            conversationState = new ParticipantConversationState
            {
                ParticipantId = participant.Id
            };
            dbContext.ParticipantConversationStates.Add(conversationState);
        }

        conversationState.State = state;
        conversationState.DataJson = null;
        conversationState.UpdatedAt = now;
        conversationState.ExpiresAt = now + StateTtl;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearStateByUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.ParticipantConversationStates
            .SingleOrDefaultAsync(value => value.Participant.TelegramUserId == telegramUserId, cancellationToken);
        if (state is null)
        {
            return;
        }

        dbContext.ParticipantConversationStates.Remove(state);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearStateAsync(
        ParticipantConversationState conversationState,
        CancellationToken cancellationToken)
    {
        dbContext.ParticipantConversationStates.Remove(conversationState);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SendOrEditAsync(
        CallbackQuery callbackQuery,
        long chatId,
        string text,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.Message is { } callbackMessage)
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

    private bool IsAdmin(long telegramUserId) =>
        campOptions.Value.AdminTelegramIds.Contains(telegramUserId);

    private static string BuildImportState(
        ExternalGameProvider provider,
        ImportTarget target) =>
        $"collections:import:{(provider == ExternalGameProvider.Bgg ? "bgg" : "tesera")}:{(target == ImportTarget.Club ? "club" : "personal")}";

    private static bool TryParseProvider(string value, out ExternalGameProvider provider)
    {
        provider = value switch
        {
            "bgg" => ExternalGameProvider.Bgg,
            "tesera" => ExternalGameProvider.Tesera,
            _ => (ExternalGameProvider)(-1)
        };
        return provider is ExternalGameProvider.Bgg or ExternalGameProvider.Tesera;
    }

    private static bool TryParseTarget(string value, out ImportTarget target)
    {
        target = value switch
        {
            "personal" => ImportTarget.Participant,
            "club" => ImportTarget.Club,
            _ => (ImportTarget)(-1)
        };
        return target is ImportTarget.Participant or ImportTarget.Club;
    }
}
