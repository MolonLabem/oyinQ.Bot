using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringPublicationService(
    AppDbContext dbContext,
    ICommunityStore communityStore,
    GatheringTelegramPublisher publisher,
    ITelegramBotClient botClient,
    IOptions<BotOptions> botOptions,
    TimeProvider timeProvider,
    ILogger<GatheringPublicationService> logger)
{
    public async Task<bool> PublishAsync(Guid publicId, CancellationToken cancellationToken)
    {
        var gathering = await dbContext.GameGatherings
            .Include(x => x.OrganizerParticipant)
            .Include(x => x.Participants).ThenInclude(x => x.Participant)
            .Include(x => x.Expansions)
            .SingleOrDefaultAsync(x => x.PublicId == publicId, cancellationToken)
            ?? throw new KeyNotFoundException("Сбор не найден.");
        var community = await communityStore.FindByKeyAsync(gathering.CommunityKey, cancellationToken)
            ?? throw new KeyNotFoundException("Сообщество не найдено.");
        gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        gathering.PublicationError = null;
        gathering.PublicationAttempts++;
        gathering.LastPublicationAttemptAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        try
        {
            if (gathering.TelegramChatId is null || gathering.TelegramMessageId is null)
            {
                var message = await publisher.PublishAsync(gathering, community, cancellationToken);
                gathering.TelegramChatId = message.Chat.Id;
                gathering.TelegramMessageId = message.Id;
            }
            else
            {
                await publisher.UpdateAsync(gathering, community, cancellationToken);
            }
            gathering.PublicationStatus = GatheringPublicationStatus.Published;
            gathering.PublicationError = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            gathering.PublicationStatus = GatheringPublicationStatus.Failed;
            gathering.PublicationError = exception.Message.Length <= 2000 ? exception.Message : exception.Message[..2000];
            await dbContext.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(exception, "Gathering {GatheringPublicId} publication failed.", publicId);
            return false;
        }
    }

    public async Task NotifyPromotionAsync(GatheringPromotion promotion, Guid gatheringPublicId,
        CancellationToken cancellationToken)
    {
        try
        {
            var communityKey = await dbContext.GameGatherings.AsNoTracking()
                .Where(x => x.PublicId == gatheringPublicId)
                .Select(x => x.CommunityKey)
                .SingleAsync(cancellationToken);
            var url = $"{botOptions.Value.PublicBaseUrl.TrimEnd('/')}/app/?community={Uri.EscapeDataString(communityKey)}&gathering={gatheringPublicId}";
            await botClient.SendMessage(promotion.TelegramUserId,
                $"{promotion.DisplayName}, для вас освободилось место в сборе.",
                replyMarkup: new InlineKeyboardMarkup([[
                    InlineKeyboardButton.WithWebApp("Открыть сбор", new WebAppInfo { Url = url })
                ]]),
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not notify promoted participant {TelegramUserId}.",
                promotion.TelegramUserId);
        }
    }
}
