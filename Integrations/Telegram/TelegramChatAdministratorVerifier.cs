using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Admin;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramChatAdministratorVerifier(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    TimeProvider timeProvider,
    ILogger<TelegramChatAdministratorVerifier> logger) : ITelegramChatAdministratorVerifier
{
    public async Task<bool> IsAdministratorAsync(long telegramChatId, long telegramUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var member = await botClient.GetChatMember(telegramChatId, telegramUserId, cancellationToken);
            await MarkBotPresentAsync(telegramChatId, cancellationToken);
            return member.Status is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;
        }
        catch (ApiRequestException exception) when (TelegramCommunityMembershipVerifier.IsChatUnavailable(exception))
        {
            await MarkBotAbsentAsync(telegramChatId, cancellationToken);
            return false;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Could not verify Telegram administrator {TelegramUserId} in chat {TelegramChatId}: {ErrorType}: {ErrorMessage}",
                telegramUserId, telegramChatId, exception.GetType().Name, exception.Message);
            logger.LogDebug(exception,
                "Telegram administrator verification failure details for {TelegramUserId} in chat {TelegramChatId}.",
                telegramUserId, telegramChatId);
            return false;
        }
    }

    public async Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(
        long telegramChatId, CancellationToken cancellationToken)
    {
        try
        {
            var administrators = await botClient.GetChatAdministrators(telegramChatId,
                returnBots: false, cancellationToken);
            await MarkBotPresentAsync(telegramChatId, cancellationToken);
            return administrators.Where(x => !x.User.IsBot).Select(x => new EligibleGroupAdministrator(
                    x.User.Id, BuildDisplayName(x.User), x.User.Username))
                .DistinctBy(x => x.TelegramUserId).ToArray();
        }
        catch (ApiRequestException exception) when (TelegramCommunityMembershipVerifier.IsChatUnavailable(exception))
        {
            await MarkBotAbsentAsync(telegramChatId, cancellationToken);
            throw new InvalidOperationException(
                "Бот больше не состоит в этой Telegram-группе. Добавьте его обратно и повторите попытку.", exception);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Could not list Telegram administrators in chat {TelegramChatId}: {ErrorType}: {ErrorMessage}",
                telegramChatId, exception.GetType().Name, exception.Message);
            logger.LogDebug(exception,
                "Telegram administrator listing failure details for chat {TelegramChatId}.", telegramChatId);
            throw new InvalidOperationException(
                "Telegram не позволил получить список администраторов группы. Проверьте права бота.", exception);
        }
    }

    private static string? BuildDisplayName(global::Telegram.Bot.Types.User user)
    {
        var value = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task MarkBotAbsentAsync(long telegramChatId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var known = await dbContext.KnownTelegramChats.SingleOrDefaultAsync(
            x => x.TelegramChatId == telegramChatId, cancellationToken);
        var changed = known is null || known.IsBotPresent;
        if (known is null)
        {
            known = new KnownTelegramChat
            {
                TelegramChatId = telegramChatId,
                FirstSeenAt = now
            };
            dbContext.KnownTelegramChats.Add(known);
        }
        known.IsBotPresent = false;
        known.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (changed)
            logger.LogInformation(
                "Telegram chat {TelegramChatId} is unavailable to the bot; excluding it from administrator discovery.",
                telegramChatId);
    }

    private async Task MarkBotPresentAsync(long telegramChatId, CancellationToken cancellationToken)
    {
        var known = await dbContext.KnownTelegramChats.SingleOrDefaultAsync(
            x => x.TelegramChatId == telegramChatId, cancellationToken);
        if (known is not { IsBotPresent: false }) return;
        known.IsBotPresent = true;
        known.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Telegram chat {TelegramChatId} is available to the bot again.", telegramChatId);
    }
}
