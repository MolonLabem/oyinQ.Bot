using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Features.Notifications;

// Called only by explicit mutations under the Camp lock, within their transaction.
public sealed class CampWishlistNotifications(AppDbContext db, TimeProvider clock)
{
    public async Task BoxChangedAsync(long campId, long owner, long bggId, CampBringCommitment commitment, CancellationToken ct)
    {
        var camp = await db.Camps.AsNoTracking().Include(x => x.BotChat).SingleAsync(x => x.Id == campId, ct);
        var wished = await db.GameWishes.Where(x => x.CommunityKey == camp.BotChatKey && x.BggId == bggId).Select(x => x.ParticipantId).ToArrayAsync(ct);
        var requested = await db.CampBringRequesters.AsNoTracking().Where(x => x.Request.CampId == campId && x.Request.BggId == bggId && x.Active).ToArrayAsync(ct);
        var ids = wished.Concat(requested.Select(x => x.ParticipantId)).Where(x => x != owner).Distinct().ToArray();
        if (ids.Length == 0) return;
        var registrations = await db.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays)
            .Where(x => x.CampId == campId && (ids.Contains(x.ParticipantId) || x.ParticipantId == owner)).ToArrayAsync(ct);
        var ownerRegistration = registrations.SingleOrDefault(x => x.ParticipantId == owner);
        if (!CampParticipationPolicy.IsRegistrationComplete(ownerRegistration, camp)) return;
        var contribution = await db.CampGameContributions.AsNoTracking().SingleAsync(x => x.CampId == campId && x.ParticipantId == owner && x.BggId == bggId && x.ItemType == CollectionItemType.BaseGame, ct);
        var availableDates = CampContributionSelectionService.EffectiveDates(contribution, ownerRegistration!);
        var existing = await db.Notifications.AsNoTracking().Where(x => x.CommunityKey == camp.BotChatKey && x.BggId == bggId
            && (x.Kind == NotificationKind.CampBoxOffered || x.Kind == NotificationKind.CampBoxConfirmed)).ToArrayAsync(ct);
        var kind = commitment == CampBringCommitment.Bringing ? NotificationKind.CampBoxConfirmed : NotificationKind.CampBoxOffered;
        foreach (var recipient in ids)
        {
            var registration = registrations.SingleOrDefault(x => x.ParticipantId == recipient);
            if (!CampParticipationPolicy.IsRegistrationComplete(registration, camp)) continue;
            var dates = registration!.SelectedDays.Select(x => x.Date).Intersect(availableDates).ToArray();
            if (dates.Length == 0 || !wished.Contains(recipient) && !requested.Any(x => x.ParticipantId == recipient && x.Dates.Any(dates.Contains))) continue;
            if (existing.FirstOrDefault(x => x.ParticipantId == recipient && x.Kind == kind) is { } earlier)
            {
                if (earlier.State is NotificationState.Pending or NotificationState.Failed or NotificationState.CannotMessageUser)
                    await UpdatePendingAsync(earlier.Id, owner, false, ct);
                continue;
            }
            if (kind == NotificationKind.CampBoxOffered && existing.Any(x => x.ParticipantId == recipient && x.Kind == NotificationKind.CampBoxConfirmed)) continue;
            var row = New(camp.BotChatKey, recipient, owner, bggId, kind);
            if (kind == NotificationKind.CampBoxConfirmed)
                foreach (var pending in existing.Where(x => x.ParticipantId == recipient && x.Kind == NotificationKind.CampBoxOffered
                    && x.State is NotificationState.Pending or NotificationState.Failed or NotificationState.CannotMessageUser))
                    await UpdatePendingAsync(pending.Id, owner, true, ct);
            db.Notifications.Add(row);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task UpdatePendingAsync(long id, long owner, bool expire, CancellationToken ct)
    {
        // A dispatcher can claim the row after the planner reads it. Never overwrite its send state.
        var pending = db.Notifications.Where(x => x.Id == id && (x.State == NotificationState.Pending
            || x.State == NotificationState.Failed || x.State == NotificationState.CannotMessageUser));
        if (db.Database.IsRelational())
        {
            if (expire) await pending.ExecuteUpdateAsync(s => s.SetProperty(x => x.State, NotificationState.Expired), ct);
            else await pending.ExecuteUpdateAsync(s => s.SetProperty(x => x.ActorParticipantId, owner), ct);
        }
        else if (await pending.SingleOrDefaultAsync(ct) is { } row)
        {
            if (expire) row.State = NotificationState.Expired;
            else row.ActorParticipantId = owner;
        }
    }

    private Notification New(string key, long recipient, long owner, long game, NotificationKind kind) => new() {
        ParticipantId = recipient, ActorParticipantId = owner, BggId = game, CommunityKey = key, Kind = kind,
        DeduplicationKey = $"{kind}:{key}:{game}:{(kind is NotificationKind.CampBringRequested or NotificationKind.CampBringDeclined ? owner : 0)}:{recipient}",
        CreatedAt = clock.GetUtcNow(), NextAttemptAt = clock.GetUtcNow()
    };

    public async Task RequestedAsync(long campId, long owner, long game, CancellationToken ct)
    {
        var key = await db.Camps.Where(x => x.Id == campId).Select(x => x.BotChatKey).SingleAsync(ct);
        var row = New(key, owner, owner, game, NotificationKind.CampBringRequested);
        if (await db.Notifications.AnyAsync(x => x.DeduplicationKey == row.DeduplicationKey, ct)) return;
        if (await PrepareAsync(row, ct)) db.Notifications.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeclinedAsync(long campId, long owner, long game, CancellationToken ct)
    {
        var key = await db.Camps.Where(x => x.Id == campId).Select(x => x.BotChatKey).SingleAsync(ct);
        var recipients = await db.CampBringRequesters.Where(x => x.Request.CampId == campId && x.Request.OwnerParticipantId == owner && x.Request.BggId == game && x.Active).Select(x => x.ParticipantId).ToArrayAsync(ct);
        var existing = await db.Notifications.Where(x => x.CommunityKey == key && x.BggId == game && x.ActorParticipantId == owner && x.Kind == NotificationKind.CampBringDeclined).Select(x => x.ParticipantId).ToArrayAsync(ct);
        foreach (var recipient in recipients.Except(existing)) db.Notifications.Add(New(key, recipient, owner, game, NotificationKind.CampBringDeclined));
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> PrepareAsync(Notification row, CancellationToken ct)
    {
        var camp = await db.Camps.AsNoTracking().Include(x => x.BotChat).SingleOrDefaultAsync(x => x.BotChatKey == row.CommunityKey, ct);
        if (camp == null || !camp.BotChat.IsActive || camp.BotChat.DeletedAt != null || camp.Status != CampStatus.Active || camp.EndsAtUtc <= clock.GetUtcNow()) return false;
        var registrations = await db.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays).Include(x => x.Participant)
            .Where(x => x.CampId == camp.Id && (x.ParticipantId == row.ParticipantId || x.ParticipantId == row.ActorParticipantId)).ToArrayAsync(ct);
        var recipient = registrations.SingleOrDefault(x => x.ParticipantId == row.ParticipantId);
        var owner = registrations.SingleOrDefault(x => x.ParticipantId == row.ActorParticipantId);
        if (!CampParticipationPolicy.IsRegistrationComplete(recipient, camp) || !CampParticipationPolicy.IsRegistrationComplete(owner, camp)) return false;
        var request = await db.CampBringRequests.AsNoTracking().Include(x => x.Requesters).SingleOrDefaultAsync(x => x.CampId == camp.Id && x.OwnerParticipantId == row.ActorParticipantId && x.BggId == row.BggId, ct);
        var contribution = await db.CampGameContributions.AsNoTracking().SingleOrDefaultAsync(x => x.CampId == camp.Id && x.ParticipantId == row.ActorParticipantId && x.BggId == row.BggId && x.ItemType == CollectionItemType.BaseGame, ct);
        var owned = await db.ParticipantCollectionItems.AsNoTracking().SingleOrDefaultAsync(x => x.ParticipantId == row.ActorParticipantId && x.BggId == row.BggId && x.ItemType == CollectionItemType.BaseGame, ct);
        var game = contribution?.ReadSnapshot().Name ?? owned?.ReadSnapshot().Name
            ?? (request == null ? null : CollectionItemSnapshotSerializer.Deserialize(request.SnapshotJson).Name);
        if (game == null) return false;
        var name = owner!.DisplayName ?? owner.Participant.PreferredDisplayName ?? owner.Participant.DisplayName;
        if (row.Kind == NotificationKind.CampBringRequested)
        {
            if (request == null || request.Declined || !request.Requesters.Any(x => x.Active) || (contribution == null && !owner.ShareCollection)) return false;
            var activeIds = request.Requesters.Where(x => x.Active).Select(x => x.ParticipantId).ToArray();
            var current = await db.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays).Where(x => x.CampId == camp.Id && activeIds.Contains(x.ParticipantId)).ToArrayAsync(ct);
            var valid = current.Where(x => CampParticipationPolicy.IsRegistrationComplete(x, camp) && request.Requesters.Any(r => r.ParticipantId == x.ParticipantId && r.Active && r.Dates.Any(d => x.SelectedDays.Any(s => s.Date == d) && owner.SelectedDays.Any(s => s.Date == d)))).ToArray();
            if (valid.Length == 0) return false;
            var bringing = await db.CampGameContributions.AsNoTracking().Where(x => x.CampId == camp.Id && x.BggId == row.BggId
                && x.ItemType == CollectionItemType.BaseGame && x.Commitment == CampBringCommitment.Bringing).ToArrayAsync(ct);
            if (bringing.Length > 0)
            {
                var providerIds = bringing.Select(x => x.ParticipantId).ToArray();
                var providers = await db.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays)
                    .Where(x => x.CampId == camp.Id && providerIds.Contains(x.ParticipantId)).ToArrayAsync(ct);
                var covered = bringing.SelectMany(x => providers.Where(p => p.ParticipantId == x.ParticipantId && CampParticipationPolicy.IsRegistrationComplete(p, camp))
                    .SelectMany(p => CampContributionSelectionService.EffectiveDates(x, p))).ToHashSet();
                if (valid.All(p => request.Requesters.Single(r => r.ParticipantId == p.ParticipantId).Dates
                    .Where(d => p.SelectedDays.Any(s => s.Date == d)).All(covered.Contains))) return false;
            }
            row.Text = $"Вас попросили привезти «{game}» на {camp.Name}. К просьбе присоединились: {valid.Length}. Откройте хотелки, чтобы посмотреть дни и ответить. Привоз коробки не обязывает организовывать партию или объяснять правила.";
            return true;
        }
        var author = request?.Requesters.SingleOrDefault(x => x.ParticipantId == row.ParticipantId && x.Active);
        if (row.Kind == NotificationKind.CampBringDeclined)
        {
            if (request?.Declined != true || author == null) return false;
            row.Text = $"{name} не сможет привезти «{game}» на {camp.Name}. В хотелках можно посмотреть другие коробки.";
            return true;
        }
        var wished = await db.GameWishes.AnyAsync(x => x.CommunityKey == row.CommunityKey && x.BggId == row.BggId && x.ParticipantId == row.ParticipantId, ct);
        // Requests to any owner are subscriptions to the need, without creating a wish.
        var requests = await db.CampBringRequesters.AsNoTracking().Where(x => x.Request.CampId == camp.Id && x.Request.BggId == row.BggId && x.ParticipantId == row.ParticipantId && x.Active).ToArrayAsync(ct);
        if (!wished && requests.Length == 0 || contribution == null || owned == null) return false;
        var dates = CampContributionSelectionService.EffectiveDates(contribution, owner).Intersect(recipient!.SelectedDays.Select(x => x.Date)).ToArray();
        if (dates.Length == 0 || (!wished && !requests.Any(x => x.Dates.Any(dates.Contains)))) return false;
        var confirmed = contribution.Commitment == CampBringCommitment.Bringing;
        if (row.Kind == NotificationKind.CampBoxConfirmed && !confirmed || row.Kind == NotificationKind.CampBoxOffered && confirmed) return false;
        var days = string.Join(", ", dates.Select(x => x.ToString("dd.MM.yyyy")));
        row.Text = confirmed ? $"Для вашей {(wished ? "хотелки" : "просьбы")} нашлась коробка: {name} привезёт «{game}» на {camp.Name}, {days}."
            : $"{name} может привезти «{game}» на {camp.Name}, {days}. Пока без окончательного подтверждения.";
        row.Text += " Откройте игру: посмотрите сборы или организуйте свой.";
        return true;
    }
}
