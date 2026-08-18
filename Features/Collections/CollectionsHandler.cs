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
    private const string BggUnavailableMessage = "BGG пока недоступен — ждём подтверждение API-доступа.\nПопробуй Tesera или добавь игру другим доступным способом.";
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
            await HandleImportInputAsync(
                message.Chat.Id,
                telegramUserId,
                conversationState,
                text,
                cancellationToken);
            return true;
        }

        if (text == "➕ Добавить игры")
        {
            await ShowAddMenuAsync(message.Chat.Id, telegramUserId, cancellationToken);
            return true;
        }

        return false;
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

        var chatId = callbackQuery.Message?.Chat.Id ?? telegramUserId;
        if (data == "collection:menu")
        {
            await ShowAddMenuAsync(callbackQuery, chatId, telegramUserId, cancellationToken);
            return true;
        }

        if (data == "collection:add:single")
        {
            if (!bggOptions.Value.IsAvailable)
            {
                await SendOrEditAsync(
                    callbackQuery,
                    chatId,
                    BggUnavailableMessage,
                    BuildAddMenu(telegramUserId),
                    cancellationToken);
                return true;
            }

            await SetStateAsync(telegramUserId, GameAddSearchState, cancellationToken);
            await SendOrEditAsync(
                callbackQuery,
                chatId,
                "Отправьте название игры или ссылку BGG вида boardgamegeek.com/boardgame/12345.",
                null,
                cancellationToken);
            return true;
        }

        var parts = data.Split(':');
        if (parts is ["collection", "import", var providerText, var targetText]
            && TryParseProvider(providerText, out var provider)
            && TryParseTarget(targetText, out var target))
        {
            if (provider == ExternalGameProvider.Bgg && !bggOptions.Value.IsAvailable)
            {
                await SendOrEditAsync(
                    callbackQuery,
                    chatId,
                    BggUnavailableMessage,
                    BuildAddMenu(telegramUserId),
                    cancellationToken);
                return true;
            }

            if (target == ImportTarget.Club
                && !campOptions.Value.AdminTelegramIds.Contains(telegramUserId))
            {
                await botClient.SendMessage(
                    chatId,
                    "Импорт коллекции клуба доступен только администратору.",
                    cancellationToken: cancellationToken);
                return true;
            }

            await SetStateAsync(
                telegramUserId,
                BuildImportState(provider, target),
                cancellationToken);

            var prompt = provider == ExternalGameProvider.Bgg
                ? "Отправьте имя пользователя BGG или ссылку на его профиль/коллекцию."
                : "Отправьте alias пользователя Tesera или ссылку на его профиль. Tesera сейчас может отвечать нестабильно; ошибка этого импорта не повлияет на другие функции бота.";
            await SendOrEditAsync(callbackQuery, chatId, prompt, null, cancellationToken);
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
        if (parts is not ["collections", "import", _, _])
        {
            await ClearStateAsync(conversationState, cancellationToken);
            return;
        }

        if (!TryParseProvider(parts[2], out var provider)
            || !TryParseTarget(parts[3], out var target))
        {
            await ClearStateAsync(conversationState, cancellationToken);
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
        catch (UnauthorizedAccessException exception)
        {
            await botClient.SendMessage(
                chatId,
                exception.Message,
                cancellationToken: cancellationToken);
            return;
        }

        await ClearStateAsync(conversationState, cancellationToken);

        var responseMessage = result.Status switch
        {
            CollectionImportEnqueueStatus.Queued
                => "⏳ Импорт поставлен в очередь. Я напишу, когда он завершится.",
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
            replyMarkup: Keyboards.MainMenu,
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

    private string BuildAddMenuText() => bggOptions.Value.IsAvailable
        ? "Как добавить игры?"
        : $"Как добавить игры?\n\n{BggUnavailableMessage}";

    private InlineKeyboardMarkup BuildAddMenu(long telegramUserId)
    {
        var rows = new List<InlineKeyboardButton[]>();

        if (bggOptions.Value.IsAvailable)
        {
            rows.Add(
            [
                InlineKeyboardButton.WithCallbackData("Одна игра", "collection:add:single")
            ]);
            rows.Add(
            [
                InlineKeyboardButton.WithCallbackData("Коллекция BGG", "collection:import:bgg:personal"),
                InlineKeyboardButton.WithCallbackData("Коллекция Tesera", "collection:import:tesera:personal")
            ]);
        }
        else
        {
            rows.Add(
            [
                InlineKeyboardButton.WithCallbackData("Коллекция Tesera", "collection:import:tesera:personal")
            ]);
        }

        if (campOptions.Value.AdminTelegramIds.Contains(telegramUserId))
        {
            rows.Add(
                bggOptions.Value.IsAvailable
                    ?
                    [
                        InlineKeyboardButton.WithCallbackData("🏢 BGG клуба", "collection:import:bgg:club"),
                        InlineKeyboardButton.WithCallbackData("🏢 Tesera клуба", "collection:import:tesera:club")
                    ]
                    :
                    [
                        InlineKeyboardButton.WithCallbackData("🏢 Tesera клуба", "collection:import:tesera:club")
                    ]);
        }

        return new InlineKeyboardMarkup(rows);
    }

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
