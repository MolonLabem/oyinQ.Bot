using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.MiniApp;

public sealed class ProfileDeletionService(AppDbContext db, TimeProvider time, GatheringNotificationService notifications)
{
    public const string AnonymousName = ParticipantPresentation.AnonymousName;

    public async Task DeleteAsync(long telegramUserId, CancellationToken ct)
    {
        await using var operation = await ParticipantOperationLock.AcquireAsync(db, telegramUserId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.DeletedProfiles.AnyAsync(x => x.TelegramUserId == telegramUserId, ct)) return;
        var now = time.GetUtcNow();
        var participant = await db.Participants.SingleOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);
        db.DeletedProfiles.Add(new DeletedProfile { TelegramUserId = telegramUserId, DeletedAt = now });
        if (participant is not null)
        {
            var id = participant.Id;
            // Camp -> gathering is the existing mutation lock order. Include invisible and deleted communities.
            var campIds = await db.Camps.Where(x => x.Registrations.Any(r => r.ParticipantId == id)
                || x.Contributions.Any(c => c.ParticipantId == id) || x.CreatedByTelegramUserId == telegramUserId)
                .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(ct);
            foreach (var campId in campIds)
                if (db.Database.IsNpgsql())
                    await db.Camps.FromSqlInterpolated($"SELECT * FROM \"Camps\" WHERE \"Id\" = {campId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
            var references = await db.GameGatherings.Where(x => x.OrganizerParticipantId == id
                || x.Participants.Any(p => p.ParticipantId == id) || x.OutcomeRecordedByParticipantId == id
                || db.GatheringPlayRecords.Any(r => r.GatheringId == x.Id && r.Players.Any(p => p.ParticipantId == id))).OrderBy(x => x.Id)
                .Select(x => new { x.PublicId, x.CommunityKey }).ToArrayAsync(ct);
            var gatherings = new List<GameGathering>();
            foreach (var reference in references)
                gatherings.Add(await GatheringWriteStore.LockAsync(db, reference.PublicId, reference.CommunityKey, ct));
            await ParticipantWriteStore.RequireActiveAsync(db, id, ct);

            participant.DeletedAt = now;
            participant.TelegramUserId = -id;
            participant.TelegramUsername = null;
            participant.DisplayName = AnonymousName;
            participant.PreferredDisplayName = null;
            participant.ActiveCommunityKey = null;
            participant.PrivateChatStartedAt = null;
            participant.TelegramDeliveryBlockedAt = null;
            participant.UpdatedAt = now;
            var withdrawals = new List<GatheringWithdrawalOutcome>();
            var cancelled = new List<Guid>();
            foreach (var gathering in gatherings)
            {
                if (gathering.OrganizerParticipantId == id)
                {
                    gathering.OrganizerDisplayNameOverride = null;
                    if (GatheringLifecycle.IsUpcoming(gathering, now))
                    {
                        GatheringRules.Cancel(gathering, "Организатор удалил профиль", now);
                        cancelled.Add(gathering.PublicId);
                    }
                }
                var membership = gathering.Participants.SingleOrDefault(x => x.ParticipantId == id);
                if (membership is not null)
                {
                    membership.DisplayNameOverride = null;
                    if (gathering.OrganizerParticipantId != id && GatheringLifecycle.IsUpcoming(gathering, now)
                        && GatheringRules.WithdrawParticipant(gathering, membership, now) is { } withdrawal)
                        withdrawals.Add(GatheringWithdrawalOutcome.Capture(gathering, withdrawal));
                }
                GatheringPublication.Request(gathering);
                var legacy = db.Entry(gathering).Property<string?>("LegacyPlayOutcomeJson");
                if (legacy.CurrentValue is { } json && JsonNode.Parse(json) is JsonObject audit)
                {
                    if (audit["players"] is JsonArray players)
                        foreach (var player in players.OfType<JsonObject>().Where(x => x["ParticipantId"]?.GetValue<long>() == id))
                            player["DisplayName"] = AnonymousName;
                    if (audit["RecordedByParticipantId"]?.GetValue<long>() == id) audit.Remove("ExternalUrl");
                    legacy.CurrentValue = audit.ToJsonString();
                }
            }
            foreach (var player in await db.GatheringPlayPlayers.Where(x => x.ParticipantId == id).ToArrayAsync(ct))
                player.DisplayName = AnonymousName;

            await RemoveAsync(db.ParticipantCollectionItems.Where(x => x.ParticipantId == id), ct);
            await RemoveAsync(db.GameWishes.Where(x => x.ParticipantId == id), ct);
            await RemoveAsync(db.CampGameContributions.Where(x => x.ParticipantId == id), ct);
            await RemoveAsync(db.CampParticipantVisibilities.Where(x => x.ParticipantId == id), ct);
            await RemoveAsync(db.CampRegistrations.Include(x => x.SelectedDays).Where(x => x.ParticipantId == id), ct);
            await RemoveAsync(db.CampBringRequesters.Where(x => x.ParticipantId == id), ct);
            await RemoveAsync(db.CampBringRequests.Include(x => x.Requesters).Where(x => x.OwnerParticipantId == id), ct);
            await RemoveAsync(db.NotificationPreferences.Where(x => x.ParticipantId == id), ct);
            await RemoveAsync(db.Notifications.Where(x => x.ParticipantId == id), ct);
            await RemoveAsync(db.ChatAdminPermissions.Where(x => x.TelegramUserId == telegramUserId), ct);
            foreach (var permission in await db.ChatAdminPermissions.Where(x => x.GrantedByTelegramUserId == telegramUserId).ToArrayAsync(ct))
                permission.GrantedByTelegramUserId = 0;
            foreach (var camp in await db.Camps.Where(x => x.CreatedByTelegramUserId == telegramUserId).ToArrayAsync(ct))
                camp.CreatedByTelegramUserId = 0;
            await RemoveAsync(db.PendingTelegramPeerSelections.Where(x => x.RequestedByTelegramUserId == telegramUserId), ct);
            await RemoveAsync(db.GatheringExternalPlayReferences.Where(x => x.AddedByParticipantId == id), ct);
            // Preserve cancelled job IDs for in-flight workers, erase all source data, and invalidate their leases.
            foreach (var import in await db.CampBggImports.Where(x => x.ParticipantId == id).ToArrayAsync(ct))
            {
                import.BggUsername = ""; import.DraftJson = null; import.ConfirmationJson = null; import.Error = null;
                import.Status = CampBggImportStatus.Cancelled; import.Stage = BggImportStage.Cancelled;
                import.CancellationRequestedAt = now; import.LeaseId = null; import.LeaseExpiresAt = null; import.UpdatedAt = now;
            }
            foreach (var notice in await db.Notifications.Where(x => x.ActorParticipantId == id).ToArrayAsync(ct))
            {
                notice.Text = "Участник удалил профиль.";
                notice.ActorParticipantId = null;
                if (notice.State is not (NotificationState.Delivered or NotificationState.DeliveryUnknown))
                    notice.State = NotificationState.Expired;
            }
            await db.SaveChangesAsync(ct);
            foreach (var publicId in cancelled) await notifications.NotifyCancellationAsync(publicId, ct);
            foreach (var withdrawal in withdrawals) await notifications.NotifyWithdrawalAsync(withdrawal, ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task RecreateAsync(TelegramMiniAppIdentity identity, CancellationToken ct)
    {
        await using var operation = await ParticipantOperationLock.AcquireAsync(db, identity.TelegramUserId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await RemoveAsync(db.DeletedProfiles.Where(x => x.TelegramUserId == identity.TelegramUserId), ct);
        await db.SaveChangesAsync(ct);
        await new ParticipantIdentityService(db, time).GetOrCreateAsync(identity.TelegramUserId,
            identity.TelegramUsername, identity.DisplayName, null, ct);
        await transaction.CommitAsync(ct);
    }

    private async Task RemoveAsync<T>(IQueryable<T> query, CancellationToken ct) where T : class =>
        db.RemoveRange(await query.ToArrayAsync(ct));
}
