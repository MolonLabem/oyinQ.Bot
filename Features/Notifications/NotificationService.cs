using Microsoft.EntityFrameworkCore;
using Npgsql;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Notifications;

public sealed record NotificationIntent(long TelegramUserId, NotificationKind Kind, string EventKey, string Text,
    string? CommunityKey = null, Guid? GatheringPublicId = null, Guid? ImportPublicId = null);

public sealed class NotificationService(AppDbContext db, TimeProvider time)
{
    public async Task EnqueueAsync(NotificationIntent intent, CancellationToken ct)
    {
        var participantId = await db.Participants.Where(x => x.TelegramUserId == intent.TelegramUserId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
        if (participantId is null) return; // A transport ID alone must never provision an identity.
        var key = $"{intent.Kind}:{intent.EventKey}:{participantId}";
        var existing = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(x => x.DeduplicationKey == key, ct);
        if (existing is not null)
        {
            if (NotificationPolicy.CanReconsider(existing.Kind, existing.State))
            {
                var now = time.GetUtcNow();
                if (db.Database.IsRelational())
                    await db.Notifications.Where(x => x.Id == existing.Id
                        && (x.State == NotificationState.SuppressedByPreference || x.State == NotificationState.Expired))
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, NotificationState.Pending)
                            .SetProperty(x => x.NextAttemptAt, now), ct);
                else
                {
                    var tracked = await db.Notifications.SingleAsync(x => x.Id == existing.Id, ct);
                    tracked.State = NotificationState.Pending; tracked.NextAttemptAt = now;
                    await db.SaveChangesAsync(ct);
                }
            }
            return;
        }
        var row = new Notification { ParticipantId = participantId.Value, Kind = intent.Kind, DeduplicationKey = key,
            Text = intent.Text, CommunityKey = intent.CommunityKey, GatheringPublicId = intent.GatheringPublicId,
            ImportPublicId = intent.ImportPublicId, CreatedAt = time.GetUtcNow(), NextAttemptAt = time.GetUtcNow() };
        db.Notifications.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Notifications_DeduplicationKey" }) { db.Entry(row).State = EntityState.Detached; }
    }
}
