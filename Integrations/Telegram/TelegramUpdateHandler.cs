using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramUpdateHandler(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    AdminHandler adminHandler,
    CommunityContextResolver communityContextResolver,
    IAdministratorStore administratorStore,
    TelegramPeerSelectionService peerSelectionService,
    IOptions<BotOptions> botOptions,
    ILogger<TelegramUpdateHandler> logger)
{
    public async Task HandleAsync(Update update, CancellationToken cancellationToken)
    {
        var callback = update.CallbackQuery;
        var user = callback?.From ?? update.Message?.From;
        if (user is null) return;

        var message = update.Message;
        var command = TelegramUpdateRouting.GetCommand(message?.Text);
        if (message is { Chat.Type: not ChatType.Private })
        {
            if (TelegramUpdateRouting.IsGroupEntryRequest(message.Text, command))
            {
                await SendPrivateEntryPointAsync(message.Chat.Id, cancellationToken);
            }
            return;
        }

        if (callback?.Message is { Chat.Type: not ChatType.Private })
        {
            await botClient.AnswerCallbackQuery(callback.Id, "Откройте OyinQ через кнопку объявления.", showAlert: true, cancellationToken: cancellationToken);
            return;
        }

        if (message is { Chat.Type: ChatType.Private, UsersShared: { } usersShared })
        {
            if (await peerSelectionService.CompleteUsersAsync(
                    usersShared.RequestId, user.Id, usersShared.Users, cancellationToken))
            {
                await botClient.SendMessage(user.Id, "Выбор получен. Вернитесь в Mini App.",
                    replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            }
            return;
        }

        if (message is { Chat.Type: ChatType.Private, ChatShared: { } chatShared })
        {
            if (await peerSelectionService.CompleteChatAsync(
                    chatShared.RequestId, user.Id, chatShared, cancellationToken))
            {
                await botClient.SendMessage(user.Id, "Группа выбрана. Вернитесь в Mini App.",
                    replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            }
            return;
        }

        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            value => value.TelegramUserId == user.Id,
            cancellationToken);
        var isAdministrator = await administratorStore.IsAdministratorAsync(user.Id, cancellationToken);
        if (participant is null && (command is "/start" or "/menu" || command == "/admin" && isAdministrator))
        {
            var now = DateTimeOffset.UtcNow;
            participant = new Participant
            {
                TelegramUserId = user.Id,
                TelegramUsername = user.Username,
                DisplayName = BuildDisplayName(user),
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.Participants.Add(participant);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (message is { Chat.Type: ChatType.Private } privateMessage && command == "/admin")
        {
            await adminHandler.HandleCommandAsync(privateMessage, user.Id, cancellationToken);
            return;
        }

        if (callback is not null)
        {
            await botClient.AnswerCallbackQuery(callback.Id, "Это действие доступно в Mini App.", cancellationToken: cancellationToken);
        }

        if (participant is null)
        {
            await botClient.SendMessage(user.Id, "Откройте OyinQ командой /start.", cancellationToken: cancellationToken);
            return;
        }

        var startContext = command == "/start" ? MiniAppStartParameter.Parse(message?.Text) : null;
        await SendMiniAppEntryAsync(participant, user.Id, user.Id, startContext, isAdministrator,
            cancellationToken);
    }

    private async Task SendPrivateEntryPointAsync(long groupChatId, CancellationToken cancellationToken)
    {
        try
        {
            var me = await botClient.GetMe(cancellationToken);
            if (!string.IsNullOrWhiteSpace(me.Username))
            {
                var community = await communityContextResolver.ResolveByChatIdAsync(groupChatId, cancellationToken);
                var parameter = community is null ? "menu" : $"community-{community.Key}";
                await botClient.SendMessage(
                    groupChatId,
                    "OyinQ работает в Mini App. В группе остаются объявления, уведомления и точки входа.",
                    replyMarkup: new InlineKeyboardMarkup([[InlineKeyboardButton.WithUrl("Открыть OyinQ", $"https://t.me/{me.Username}?start={parameter}")]]),
                    cancellationToken: cancellationToken);
                return;
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(exception, "Could not build private bot deep link.");
        }

        await botClient.SendMessage(groupChatId, "Откройте личный чат с ботом, чтобы продолжить.", cancellationToken: cancellationToken);
    }

    private async Task SendMiniAppEntryAsync(
        Participant participant,
        long privateChatId,
        long telegramUserId,
        MiniAppStartContext? startContext,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BotCommunity> communities;
        try
        {
            communities = startContext is not null
                ? (await communityContextResolver.ResolveAuthorizedAsync(startContext.CommunityKey, telegramUserId, cancellationToken) is { } community ? [community] : [])
                : await communityContextResolver.ResolveAuthorizedAsync(telegramUserId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not verify OyinQ community access for {TelegramUserId}.", telegramUserId);
            communities = [];
        }

        if (communities.Count == 0 && !isAdministrator)
        {
            await botClient.SendMessage(privateChatId, "Не удалось подтвердить доступ к сообществу OyinQ. Откройте бота кнопкой из нужного группового чата.", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            return;
        }

        if (communities.Count == 1)
        {
            participant.ActiveCommunityKey = communities[0].Key;
            participant.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var rows = communities.Select(community => (IEnumerable<InlineKeyboardButton>)[
            InlineKeyboardButton.WithWebApp(
                communities.Count == 1 ? "Открыть OyinQ" : community.Name,
                new WebAppInfo { Url = BuildMiniAppUrl(community, startContext?.GatheringPublicId) })
        ]).ToList();
        if (isAdministrator)
        {
            rows.Add([
                InlineKeyboardButton.WithWebApp("Админ-панель",
                    new WebAppInfo { Url = $"{botOptions.Value.PublicBaseUrl.TrimEnd('/')}/app/?admin=1" })
            ]);
        }
        var text = communities.Count == 0
            ? "Управление OyinQ доступно в админ-панели."
            : communities.Count == 1
            ? $"🎲 {communities[0].Name}\n\nВсе основные действия доступны в Mini App."
            : "Выберите сообщество:";
        await botClient.SendMessage(privateChatId, text, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: cancellationToken);
    }

    private string BuildMiniAppUrl(BotCommunity community, Guid? gatheringPublicId)
    {
        var url = $"{botOptions.Value.PublicBaseUrl.TrimEnd('/')}/app/?community={Uri.EscapeDataString(community.Key)}";
        return gatheringPublicId is null ? url : $"{url}&gathering={gatheringPublicId}";
    }

    private static string BuildDisplayName(User user)
    {
        var name = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(name) ? user.Username ?? user.Id.ToString() : name;
    }
}
