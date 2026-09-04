using oyinQ.Bot.Data.Entities;
using Telegram.Bot;

namespace oyinQ.Bot.Integrations.Telegram;

public sealed class PrivateChatCapability(ITelegramBotClient bot, ILogger<PrivateChatCapability> logger)
{
    public async Task<string?> StartUrlAsync(Participant participant, string? communityKey,
        Guid? gatheringId, CancellationToken cancellationToken)
    {
        if (participant.PrivateChatStartedAt is not null && participant.TelegramDeliveryBlockedAt is null) return null;
        try
        {
            var me = await bot.GetMe(cancellationToken);
            return TelegramBotDeepLinks.BuildStart(me.Username!, communityKey is null ? "menu" : gatheringId is { } id
                ? MiniAppStartParameter.ForGathering(communityKey, id)
                : MiniAppStartParameter.ForCommunity(communityKey));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not resolve notification setup link.");
            return null;
        }
    }
}
