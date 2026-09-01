using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Collections;
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
    ManagedCommunityService managedCommunities,
    IAdminAuthorizationService adminAuthorization,
    TelegramPeerSelectionService peerSelectionService,
    CampBggImportCoordinator campImports,
    MiniAppLinkBuilder links,
    ILogger<TelegramUpdateHandler> logger)
{
    public async Task HandleAsync(Update update, CancellationToken cancellationToken)
    {
        await TrackKnownChatAsync(update, cancellationToken);
        if (update.Message is { MigrateToChatId: { } migrateTo } toMessage)
        {
            await HandleChatMigrationAsync(toMessage.Chat.Id, migrateTo, cancellationToken);
            return;
        }
        if (update.Message is { MigrateFromChatId: { } migrateFrom } fromMessage)
        {
            await HandleChatMigrationAsync(migrateFrom, fromMessage.Chat.Id, cancellationToken);
            return;
        }
        var callback = update.CallbackQuery;
        var user = callback?.From ?? update.Message?.From;
        if (user is null) return;
        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            value => value.TelegramUserId == user.Id, cancellationToken);
        if (participant is not null && ParticipantIdentityPolicy.RefreshTrustedPresentation(participant,
                user.Username, BuildDisplayName(user), DateTimeOffset.UtcNow))
            await dbContext.SaveChangesAsync(cancellationToken);

        if (callback is not null && CampImportCallbackData.TryParse(callback.Data, out var importId,
                out var resolution))
        {
            try
            {
                await campImports.ResolveBaseDuplicatesFromTelegramAsync(importId, user.Id, resolution,
                    cancellationToken);
                await botClient.AnswerCallbackQuery(callback.Id,
                    resolution == CampImportOverrideResolution.AddPersonalCopies
                        ? "Личные копии добавлены." : "Оставлено без изменений.",
                    cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Camp import callback {ImportId} failed for {TelegramUserId}.",
                    importId, user.Id);
                await botClient.AnswerCallbackQuery(callback.Id, exception.Message, showAlert: true,
                    cancellationToken: cancellationToken);
            }
            return;
        }

        var message = update.Message;
        var command = TelegramUpdateRouting.GetCommand(message?.Text);
        if (message is { Chat.Type: not ChatType.Private })
        {
            if (TelegramUpdateRouting.IsGroupEntryRequest(message.Text, command))
            {
                await SendGroupEntryPointAsync(message.Chat.Id, user.Id, cancellationToken);
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

        var isAdministrator = await adminAuthorization.CanOpenAdminPanelAsync(user.Id, cancellationToken);
        if (participant is null && (command is "/start" or "/menu" or "/help"
                || command == "/admin" && isAdministrator))
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


        if (message is { Chat.Type: ChatType.Private } && command == "/privacy")
        {
            await SendPrivacyAsync(user.Id, cancellationToken);
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

        if (command is not ("/start" or "/menu" or "/help")) return;

        var startContext = command == "/start" ? MiniAppStartParameter.Parse(message?.Text) : null;
        await SendMiniAppEntryAsync(participant, user.Id, user.Id, startContext, isAdministrator,
            command == "/start",
            command == "/help" ? TelegramEntryText.Help : null,
            cancellationToken);
    }

    private async Task HandleChatMigrationAsync(long oldChatId, long newChatId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await managedCommunities.MigrateTelegramChatAsync(oldChatId, newChatId,
                cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var oldKnown = await dbContext.KnownTelegramChats.SingleOrDefaultAsync(
                x => x.TelegramChatId == oldChatId, cancellationToken);
            if (oldKnown is not null)
            {
                oldKnown.IsBotPresent = false;
                oldKnown.UpdatedAt = now;
            }
            if (!await dbContext.KnownTelegramChats.AnyAsync(x => x.TelegramChatId == newChatId,
                    cancellationToken))
                dbContext.KnownTelegramChats.Add(new KnownTelegramChat
                {
                    TelegramChatId = newChatId, Title = oldKnown?.Title, Username = oldKnown?.Username,
                    IsBotPresent = true, FirstSeenAt = now, UpdatedAt = now
                });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (result.Updated)
                logger.LogInformation("Telegram chat migrated from {OldChatId} to {NewChatId} for {CommunityKey}.",
                    oldChatId, newChatId, result.CommunityKey);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception,
                "Telegram chat migration from {OldChatId} to {NewChatId} was rejected.", oldChatId, newChatId);
        }
    }

    private async Task TrackKnownChatAsync(Update update, CancellationToken cancellationToken)
    {
        var chat = update.MyChatMember?.Chat ?? update.ChatMember?.Chat ?? update.Message?.Chat;
        if (chat is null || chat.Type is not ChatType.Group and not ChatType.Supergroup) return;
        var now = DateTimeOffset.UtcNow;
        var known = await dbContext.KnownTelegramChats.SingleOrDefaultAsync(
            x => x.TelegramChatId == chat.Id, cancellationToken);
        if (known is null)
        {
            known = new KnownTelegramChat { TelegramChatId = chat.Id, FirstSeenAt = now };
            dbContext.KnownTelegramChats.Add(known);
        }
        known.Title = chat.Title;
        known.Username = chat.Username;
        known.IsBotPresent = update.MyChatMember is not { } membership
            || membership.NewChatMember.Status is not ChatMemberStatus.Left and not ChatMemberStatus.Kicked;
        known.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SendGroupEntryPointAsync(long groupChatId, long telegramUserId,
        CancellationToken cancellationToken)
    {
        var community = await dbContext.OyinQCommunities.AsNoTracking()
            .SingleOrDefaultAsync(value => value.TelegramChatId == groupChatId, cancellationToken);
        var isAdministrator = await adminAuthorization.CanOpenAdminPanelAsync(telegramUserId, cancellationToken);
        string? runtimeUsername = null;
        try
        {
            var me = await botClient.GetMe(cancellationToken);
            runtimeUsername = me.Username;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(exception, "Could not build private bot deep link.");
        }

        var entry = TelegramGroupEntryPresentation.Build(community?.Key, community?.Name,
            isAdministrator, runtimeUsername);
        var keyboard = entry.ButtonUrl is not null && entry.ButtonText is not null
            ? new InlineKeyboardMarkup([[
                InlineKeyboardButton.WithUrl(entry.ButtonText, entry.ButtonUrl)
            ]])
            : null;
        await botClient.SendMessage(groupChatId, entry.Text, replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private Task SendPrivacyAsync(long privateChatId, CancellationToken cancellationToken) =>
        botClient.SendMessage(privateChatId, TelegramEntryText.Privacy,
            replyMarkup: new InlineKeyboardMarkup([[
                InlineKeyboardButton.WithUrl("Открыть", links.Privacy())
            ]]), cancellationToken: cancellationToken);

    private async Task SendMiniAppEntryAsync(
        Participant participant,
        long privateChatId,
        long telegramUserId,
        MiniAppStartContext? startContext,
        bool isAdministrator,
        bool includeWelcome,
        string? overrideText,
        CancellationToken cancellationToken)
    {
        await EnsurePrivateMenuButtonAsync(privateChatId, cancellationToken);
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

        if (communities.Count == 0 && !isAdministrator && overrideText is null)
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
                new WebAppInfo { Url = startContext?.GatheringPublicId is { } gatheringId
                    ? links.Gathering(community.Key, gatheringId) : links.Community(community.Key) })
        ]).ToList();
        if (overrideText is not null && rows.Count == 0)
        {
            rows.Add([
                InlineKeyboardButton.WithWebApp("Открыть OyinQ",
                    new WebAppInfo { Url = links.App() })
            ]);
        }
        if (isAdministrator)
        {
            rows.Add([
                InlineKeyboardButton.WithWebApp("Админ-панель",
                    new WebAppInfo { Url = links.Admin() })
            ]);
        }
        var text = overrideText ?? (includeWelcome
            ? BuildWelcomeText(communities, isAdministrator)
            : communities.Count == 0
                ? "Управление OyinQ доступно в админ-панели."
                : communities.Count == 1
                    ? $"🎲 {communities[0].Name}\n\nОткройте OyinQ кнопкой ниже."
                    : "Выберите сообщество:");
        await botClient.SendMessage(privateChatId, text, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: cancellationToken);
    }

    private async Task EnsurePrivateMenuButtonAsync(long privateChatId, CancellationToken cancellationToken)
    {
        try
        {
            await botClient.SetChatMenuButton(
                privateChatId,
                new MenuButtonWebApp
                {
                    Text = "Открыть OyinQ",
                    WebApp = new WebAppInfo { Url = links.App() }
                },
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not configure Mini App menu button for {TelegramUserId}.",
                privateChatId);
        }
    }

    private static string BuildWelcomeText(IReadOnlyList<BotCommunity> communities, bool isAdministrator)
    {
        var destination = communities.Count switch
        {
            0 when isAdministrator => "Откройте админ-панель, чтобы настроить клубы и кэмпы.",
            1 => $"Сейчас доступно сообщество: {communities[0].Name}.",
            _ => "Выберите нужное сообщество кнопкой ниже."
        };
        return $"""
            👋 Добро пожаловать в OyinQ!

            Здесь можно:
            • смотреть коллекцию и находить игры;
            • создавать сборы и присоединяться к ним;
            • отмечать игры, которые вы привезёте в кэмп.

            {destination}
            Кнопка «Открыть OyinQ» также останется рядом с полем ввода в этом чате.
            """;
    }

    private static string BuildDisplayName(User user)
    {
        var name = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(name) ? user.Username ?? user.Id.ToString() : name;
    }
}
