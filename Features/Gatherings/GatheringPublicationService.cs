using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace oyinQ.Bot.Features.Gatherings;

public sealed class GatheringPublicationService(
    AppDbContext dbContext, ICommunityStore communityStore, GatheringTelegramPublisher publisher,
    TimeProvider timeProvider, ILogger<GatheringPublicationService> logger)
{
    public async Task<bool> PublishAsync(Guid publicId, CancellationToken cancellationToken)
    {
        Guid attempt;
        long revision;
        GameGathering snapshot;
        // Callers commit domain mutations first. Reload rather than publish their previously tracked roster.
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var row = await LockAsync(publicId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            if (row.PublicationStatus is GatheringPublicationStatus.Preparing or GatheringPublicationStatus.Delivering)
            {
                if (row.PublicationLeaseExpiresAt > now) return false;
                row.PublicationStatus = row.PublicationStatus == GatheringPublicationStatus.Delivering && row.TelegramMessageId is null
                    ? GatheringPublicationStatus.DeliveryUnknown : GatheringPublicationStatus.Failed;
                row.PublicationAttemptId = null;
                row.PublicationLeaseExpiresAt = null;
                row.PublicationError = row.PublicationStatus == GatheringPublicationStatus.DeliveryUnknown
                    ? "Не удалось подтвердить отправку объявления. Проверьте сообщения в группе; повторная отправка отключена."
                    : "Подготовка или обновление объявления прерваны. Повторите публикацию.";
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return false;
            }
            if (row.PublicationStatus == GatheringPublicationStatus.DeliveryUnknown) return false;
            if (row.PublicationStatus == GatheringPublicationStatus.Published) return true;
            if (row.TelegramMessageId is null && (row.StartsAtUtc <= now || GatheringLifecycle.IsTerminal(row.Status)))
            {
                row.PublicationStatus = GatheringPublicationStatus.NotRequired;
                row.PublicationError = null;
                await dbContext.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
                return false;
            }
            attempt = Guid.NewGuid(); revision = row.PublicationRevision;
            row.PublicationAttemptId = attempt;
            row.PublicationStatus = GatheringPublicationStatus.Preparing;
            row.PublicationLeaseExpiresAt = now.AddMinutes(2);
            row.LastPublicationAttemptAt = now;
            row.PublicationAttempts++;
            row.PublicationError = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            snapshot = await dbContext.GameGatherings.AsNoTracking().Include(x => x.OrganizerParticipant)
                .Include(x => x.Participants).ThenInclude(x => x.Participant).Include(x => x.Guests).Include(x => x.Expansions)
                .SingleAsync(x => x.PublicId == publicId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var sending = false;
        try
        {
            var community = await communityStore.FindByKeyAsync(snapshot.CommunityKey, cancellationToken);
            if (community is null && snapshot.TelegramMessageId is not null)
                community = (await dbContext.OyinQCommunities.AsNoTracking().Include(x => x.Camp)
                    .SingleAsync(x => x.Key == snapshot.CommunityKey, cancellationToken)).ToBotCommunity();
            if (community is null) throw new InvalidOperationException("Сообщество больше не принимает объявления.");
            Func<CancellationToken, Task<Message>>? send = null;
            Func<CancellationToken, Task>? edit = null;
            if (snapshot.TelegramMessageId is null) send = await publisher.PrepareNewAsync(snapshot, community, cancellationToken);
            else edit = await publisher.PrepareUpdateAsync(snapshot, community, cancellationToken);

            await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                var row = await LockAsync(publicId, cancellationToken);
                if (row.PublicationAttemptId != attempt || row.PublicationStatus != GatheringPublicationStatus.Preparing) return false;
                if (row.PublicationRevision != revision || row.PublicationLeaseExpiresAt <= timeProvider.GetUtcNow())
                {
                    row.PublicationStatus = GatheringPublicationStatus.Pending;
                    row.PublicationAttemptId = null; row.PublicationLeaseExpiresAt = null;
                    await dbContext.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
                    return false;
                }
                row.PublicationStatus = GatheringPublicationStatus.Delivering;
                row.PublicationLeaseExpiresAt = timeProvider.GetUtcNow().AddMinutes(2);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            sending = true;
            Message? message = null;
            if (send is not null) message = await send(cancellationToken);
            else await edit!(cancellationToken);
            await FinishAsync(publicId, attempt, revision, message, null, false, CancellationToken.None);
            return true;
        }
        catch (Exception e)
        {
            var unknown = sending && snapshot.TelegramMessageId is null
                && !(e is ApiRequestException api && api.ErrorCode is >= 400 and < 500);
            await FinishAsync(publicId, attempt, revision, null, e, unknown, CancellationToken.None);
            logger.LogWarning(e, "Gathering {GatheringPublicId} publication failed at {Stage}.", publicId, sending ? "send" : "preparation");
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private async Task FinishAsync(Guid publicId, Guid attempt, long revision, Message? message, Exception? error, bool unknown, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        var row = await LockAsync(publicId, ct);
        // An expired sender cannot overwrite a replacement attempt. A known late receipt is still useful.
        if (row.PublicationAttemptId != attempt && !(message is not null && row.PublicationStatus == GatheringPublicationStatus.DeliveryUnknown)) return;
        if (message is not null) { row.TelegramChatId = message.Chat.Id; row.TelegramMessageId = message.Id; }
        row.PublicationStatus = error is null
            ? row.PublicationRevision == revision ? GatheringPublicationStatus.Published : GatheringPublicationStatus.Pending
            : unknown ? GatheringPublicationStatus.DeliveryUnknown : GatheringPublicationStatus.Failed;
        row.PublicationAttemptId = null; row.PublicationLeaseExpiresAt = null;
        row.PublicationError = error is null ? null : unknown
            ? "Не удалось подтвердить отправку объявления. Проверьте сообщения в группе; повторная отправка отключена."
            : "Не удалось подготовить или обновить объявление. Повторите публикацию.";
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<GameGathering> LockAsync(Guid id, CancellationToken ct)
    {
        var query = dbContext.Database.IsRelational()
            ? dbContext.GameGatherings.FromSqlInterpolated($"SELECT * FROM \"GameGatherings\" WHERE \"PublicId\" = {id} FOR UPDATE")
            : dbContext.GameGatherings.Where(x => x.PublicId == id);
        var row = await query.SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException("Сбор не найден.");
        await dbContext.Entry(row).ReloadAsync(ct);
        return row;
    }
}
