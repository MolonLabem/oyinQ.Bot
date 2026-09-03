using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class TelegramCommunityMembershipVerifier(
    AppDbContext dbContext,
    ITelegramBotClient botClient,
    TimeProvider timeProvider,
    ILogger<TelegramCommunityMembershipVerifier> logger)
    : ICommunityMembershipVerifier
{
    private static readonly TimeSpan AbsenceProbeInterval = TimeSpan.FromMinutes(15);

    public async Task<bool> IsMemberAsync(
        long telegramChatId,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var known = await dbContext.KnownTelegramChats.SingleOrDefaultAsync(
            value => value.TelegramChatId == telegramChatId, cancellationToken);
        if (known is { IsBotPresent: false } && now - known.UpdatedAt < AbsenceProbeInterval)
        {
            return false;
        }

        try
        {
            var member = await botClient.GetChatMember(
                telegramChatId,
                telegramUserId,
                cancellationToken);

            if (known is { IsBotPresent: false })
            {
                known.IsBotPresent = true;
                known.UpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return member.Status is ChatMemberStatus.Creator
                or ChatMemberStatus.Administrator
                or ChatMemberStatus.Member
                or ChatMemberStatus.Restricted;
        }
        catch (ApiRequestException exception) when (IsChatUnavailable(exception))
        {
            known ??= new KnownTelegramChat
            {
                TelegramChatId = telegramChatId,
                FirstSeenAt = now
            };
            if (dbContext.Entry(known).State == EntityState.Detached)
            {
                dbContext.KnownTelegramChats.Add(known);
            }
            known.IsBotPresent = false;
            known.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Telegram chat {TelegramChatId} is unavailable to the bot; excluding it from community access until the next availability probe.",
                telegramChatId);
            return false;
        }
    }

    public static bool IsChatUnavailable(ApiRequestException exception)
    {
        var message = exception.Message.ToLowerInvariant();
        return (exception.ErrorCode == 400
                && message.Contains("chat not found", StringComparison.Ordinal))
               || (exception.ErrorCode == 403
                   && (message.Contains("bot was kicked", StringComparison.Ordinal)
                       || message.Contains("bot is not a member", StringComparison.Ordinal)));
    }
}
