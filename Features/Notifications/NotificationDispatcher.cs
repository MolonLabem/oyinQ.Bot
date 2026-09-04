using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Features.Notifications;

public sealed record NotificationReceipt(int? MessageId, string? ErrorCategory = null, bool Retryable = false, bool CannotMessage = false, bool Uncertain = false);
public interface INotificationTransport
{
    Task<NotificationReceipt> SendAsync(Notification notification, Participant recipient, CancellationToken ct);
}

public sealed class NotificationDispatcher(AppDbContext db, TimeProvider time, INotificationTransport transport, Features.Catalog.GameProviderService? providers = null)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        // A crashed sender may have reached Telegram. Never blindly resend an uncertain delivery.
        var expiredLease = db.Notifications.Where(x => x.State == NotificationState.Delivering && x.LeaseExpiresAt <= now);
        if (db.Database.IsRelational())
            await expiredLease.ExecuteUpdateAsync(s => s.SetProperty(x => x.State, NotificationState.DeliveryUnknown)
                .SetProperty(x => x.LastErrorCategory, "delivery_outcome_unknown"), ct);
        else
        {
            var abandoned = await expiredLease.ToArrayAsync(ct);
            foreach (var item in abandoned) { item.State = NotificationState.DeliveryUnknown; item.LastErrorCategory = "delivery_outcome_unknown"; }
            if (abandoned.Length > 0) await db.SaveChangesAsync(ct);
        }
        Notification? row;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var query = db.Database.IsRelational()
                ? db.Notifications.FromSqlInterpolated($$"""
                    SELECT n.* FROM "Notifications" n JOIN "Participants" p ON p."Id" = n."ParticipantId"
                    WHERE n."NextAttemptAt" <= {{now}} AND
                    (n."State" IN (0, 2) OR (n."State" = 4 AND p."PrivateChatStartedAt" IS NOT NULL
                     AND p."TelegramDeliveryBlockedAt" IS NULL AND (n."LastAttemptAt" IS NULL OR p."PrivateChatStartedAt" > n."LastAttemptAt")))
                    ORDER BY n."NextAttemptAt", n."Id" FOR UPDATE OF n SKIP LOCKED LIMIT 1
                    """)
                : db.Notifications.Where(x => x.NextAttemptAt <= now && (x.State == NotificationState.Pending || x.State == NotificationState.Failed
                    || (x.State == NotificationState.CannotMessageUser && x.Participant.PrivateChatStartedAt != null
                    && x.Participant.TelegramDeliveryBlockedAt == null && (x.LastAttemptAt == null || x.Participant.PrivateChatStartedAt > x.LastAttemptAt))))
                    .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.Id).Take(1);
            row = await query.Include(x => x.Participant).SingleOrDefaultAsync(ct);
            if (row is null) { await transaction.CommitAsync(ct); return false; }
            var prefs = await db.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.ParticipantId == row.ParticipantId, ct)
                ?? new NotificationPreferences();
            if (!NotificationPolicy.Allows(row.Kind, prefs)) row.State = NotificationState.SuppressedByPreference;
            else if (row.Kind == NotificationKind.WaitlistPromotion && !await PromotionStillValidAsync(row, now, ct)) row.State = NotificationState.Expired;
            else if (row.Kind == NotificationKind.Reminder && !await PrepareReminderAsync(row, prefs, now, ct)) { }
            else if (row.Kind == NotificationKind.OrganizerMissingProvider && !await ProviderStillNeeded(row, now, ct)) row.State = NotificationState.Expired;
            else if (row.Participant.PrivateChatStartedAt is null || row.Participant.TelegramDeliveryBlockedAt is not null)
            { row.State = NotificationState.CannotMessageUser; row.LastAttemptAt = now; row.LastErrorCategory = "private_chat_unavailable"; }
            else
            { row.State = NotificationState.Delivering; row.AttemptCount++; row.LastAttemptAt = now; row.LeaseExpiresAt = now.AddMinutes(2); }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        if (row.State != NotificationState.Delivering) return true;
        var receipt = await transport.SendAsync(row, row.Participant, ct);
        row.LeaseExpiresAt = null;
        row.LastErrorCategory = receipt.ErrorCategory;
        if (receipt.MessageId is { } messageId)
        { row.State = NotificationState.Delivered; row.TelegramMessageId = messageId; row.DeliveredAt = time.GetUtcNow(); row.Participant.TelegramDeliveryBlockedAt = null; }
        else if (receipt.CannotMessage)
        { row.State = NotificationState.CannotMessageUser; row.Participant.TelegramDeliveryBlockedAt = time.GetUtcNow(); }
        else if (receipt.Uncertain) row.State = NotificationState.DeliveryUnknown;
        else
        { row.State = NotificationState.Failed; row.NextAttemptAt = receipt.Retryable && row.AttemptCount < 5
            ? time.GetUtcNow().AddMinutes(Math.Pow(2, row.AttemptCount)) : DateTimeOffset.MaxValue; }
        // Keep the observed response durable even if the HTTP request that initiated work was cancelled.
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    private async Task<bool> ProviderStillNeeded(Notification row, DateTimeOffset now, CancellationToken ct)
    {
        var gathering = await db.GameGatherings.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == row.GatheringPublicId, ct);
        return gathering is not null && GatheringLifecycle.IsUpcoming(gathering, now) && providers is not null
            && !(await providers.ForGatheringAsync(gathering, row.ParticipantId, ct)).IsConfirmed;
    }

    private async Task<bool> PromotionStillValidAsync(Notification row, DateTimeOffset now, CancellationToken ct)
    {
        var gathering = await db.GameGatherings.AsNoTracking().Include(x => x.Participants).Include(x => x.Community)
            .SingleOrDefaultAsync(x => x.PublicId == row.GatheringPublicId && x.CommunityKey == row.CommunityKey, ct);
        if (gathering is null || !gathering.Community.IsActive || gathering.Community.DeletedAt is not null
            || !GatheringLifecycle.IsUpcoming(gathering, now)) return false;
        var signup = gathering.Participants.SingleOrDefault(x => x.ParticipantId == row.ParticipantId
            && x.Status == GatheringParticipationStatus.Confirmed);
        return signup is not null && row.DeduplicationKey ==
            $"{NotificationKind.WaitlistPromotion}:{gathering.PublicId:N}:{signup.JoinedAt.UtcTicks}:{row.ParticipantId}";
    }

    private async Task<bool> PrepareReminderAsync(Notification row, NotificationPreferences prefs, DateTimeOffset now, CancellationToken ct)
    {
        var gathering = await db.GameGatherings.AsNoTracking().Include(x => x.Participants)
            .Include(x => x.Community).SingleOrDefaultAsync(x => x.PublicId == row.GatheringPublicId, ct);
        if (gathering is null || !GatheringLifecycle.ScheduledStatuses.Contains(gathering.Status) || gathering.StartsAtUtc <= now
            || (gathering.OrganizerParticipantId != row.ParticipantId && !gathering.Participants.Any(x => x.ParticipantId == row.ParticipantId
                && x.Status == GatheringParticipationStatus.Confirmed)))
        { row.State = NotificationState.Expired; return false; }
        var due = gathering.StartsAtUtc.AddMinutes(-prefs.ReminderLeadMinutes);
        if (due > now) { row.State = NotificationState.Pending; row.NextAttemptAt = due; return false; }
        row.Text = $"Скоро сбор «{GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson).Name}»: "
            + GatheringPresentationService.FormatLocalDateTime(gathering.StartsAtUtc, gathering.Community.TimeZoneId) + ".";
        return true;
    }
}
