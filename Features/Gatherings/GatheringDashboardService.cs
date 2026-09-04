using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Notifications;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record DashboardGathering(Guid PublicId, string CommunityKey, string Community, string GameName,
    DateTimeOffset StartsAtUtc, string LocalDateTime, bool IsOrganizer, bool IsToday, int? WaitlistPosition,
    bool BelowMinimum, bool FullWithWaitlist, bool StartingSoon, bool RecentlyCancelled,
    bool PublicationFailed, int DeliveryProblems, GameProviderResponse Provider, int NotificationUnavailableParticipants, RecruitmentState? Recruitment = null);
public sealed record CampDashboardContext(int RegisteredToday, int GatheringsToday, int BringingGames, int AvailableGames);
public sealed record GatheringDashboard(IReadOnlyList<DashboardGathering> Items, bool HasMore, CampDashboardContext? Camp = null);

public sealed class GatheringDashboardService(AppDbContext db, GameProviderService providers, TimeProvider time)
{
    private IQueryable<GameGathering> Query => db.GameGatherings.AsNoTracking().Include(x => x.Community)
        .Include(x => x.OrganizerParticipant).Include(x => x.Participants).ThenInclude(x => x.Participant).Include(x => x.Guests);

    public async Task<GatheringDashboard> PersonalAsync(long participantId, string[] keys, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var schedule = ProfileGatheringQuery.Apply(Query, participantId, keys, now);
        var scheduledIds = await schedule.Select(x => x.Id).ToArrayAsync(ct);
        var core = await GatheringListQuery.Apply(Query.Where(x => keys.Contains(x.CommunityKey)
            && (scheduledIds.Contains(x.Id) || x.Participants.Any(p => p.ParticipantId == participantId && p.Status == GatheringParticipationStatus.Waitlisted))),
            GatheringListScope.Upcoming, now).Take(201).ToArrayAsync(ct);
        var coreIds = core.Select(x => x.Id).ToArray();
        var possibleProviders = await GatheringListQuery.Apply(Query.Where(x => keys.Contains(x.CommunityKey)
            && x.Community.Mode == Common.Options.BotMode.Camp && !coreIds.Contains(x.Id)), GatheringListScope.Upcoming, now).Take(201).ToArrayAsync(ct);
        var rows = core.Take(200).Concat(possibleProviders.Take(200)).OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).ToArray();
        var providerStates = await providers.ForGatheringsAsync(rows, participantId, ct);
        var result = new List<DashboardGathering>();
        foreach (var g in rows)
        {
            var item = Present(g, participantId, 0, providerStates[g.PublicId]);
            if (scheduledIds.Contains(g.Id) || item.WaitlistPosition <= 3 || item.Provider.CanBring && !item.Provider.IsConfirmed) result.Add(item);
        }
        return new(result, core.Length > 200 || possibleProviders.Length > 200);
    }

    public async Task<GatheringDashboard> OrganizerAsync(long participantId, string key, bool canAdminister, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var upcoming = GatheringListQuery.Apply(Query, GatheringListScope.Upcoming, now).Select(x => x.Id);
        var rows = await Query.Where(x => x.CommunityKey == key && (canAdminister || x.OrganizerParticipantId == participantId)
            && (upcoming.Contains(x.Id) || x.Status == GatheringStatus.Cancelled && x.CancelledAt >= now.AddDays(-2)))
            .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).Take(201).ToArrayAsync(ct);
        var ids = rows.Select(x => x.PublicId).ToArray();
        var errors = await db.Notifications.AsNoTracking().Where(x => x.GatheringPublicId != null && ids.Contains(x.GatheringPublicId.Value)
            && (x.State == NotificationState.CannotMessageUser || x.State == NotificationState.Failed || x.State == NotificationState.DeliveryUnknown))
            .Select(x => new { x.GatheringPublicId, x.Kind }).ToArrayAsync(ct);
        var result = new List<DashboardGathering>();
        var providerStates = await providers.ForGatheringsAsync(rows.Take(200), participantId, ct);
        foreach (var g in rows.Take(200)) result.Add(Present(g, participantId,
            errors.Count(x => x.GatheringPublicId == g.PublicId && NotificationPolicy.IsEssential(x.Kind)), providerStates[g.PublicId]));
        CampDashboardContext? context = null;
        if (canAdminister)
        {
            var camp = await db.Camps.AsNoTracking().Include(x => x.BotChat).SingleOrDefaultAsync(x => x.BotChatKey == key, ct);
            if (camp is not null)
            {
                var today = CommunityTime.LocalDate(now, camp.BotChat.TimeZoneId);
                var registrations = await db.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays)
                    .Where(x => x.CampId == camp.Id).ToArrayAsync(ct);
                var registeredIds = registrations.Where(x => CampParticipationPolicy.IsRegistrationComplete(x, camp)
                    && x.SelectedDays.Any(d => d.Date == today)).Select(x => x.ParticipantId).ToArray();
                var registered = registeredIds.Length;
                var contributions = await db.CampGameContributions.Where(x => x.CampId == camp.Id && registeredIds.Contains(x.ParticipantId))
                    .Select(x => new { x.BggId, x.Commitment }).ToArrayAsync(ct);
                var bringing = contributions.Where(x => x.Commitment == CampBringCommitment.Bringing).Select(x => x.BggId).ToHashSet();
                context = new(registered, result.Count(x => x.IsToday && !x.RecentlyCancelled), bringing.Count,
                    contributions.Where(x => !bringing.Contains(x.BggId)).Select(x => x.BggId).Distinct().Count());
            }
        }
        return new(result, rows.Length > 200, context);
    }

    private DashboardGathering Present(GameGathering g, long participantId, int deliveryProblems, GameProviderResponse provider)
    {
        var now = time.GetUtcNow();
        var wait = g.Participants.Where(x => x.Status == GatheringParticipationStatus.Waitlisted).OrderBy(x => x.JoinedAt).ThenBy(x => x.Id).ToArray();
        var position = Array.FindIndex(wait, x => x.ParticipantId == participantId);
        return new(g.PublicId, g.CommunityKey, g.Community.Name, GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson).Name,
            g.StartsAtUtc, GatheringPresentationService.FormatLocalDateTime(g.StartsAtUtc, g.Community.TimeZoneId),
            g.OrganizerParticipantId == participantId, CommunityTime.LocalDate(g.StartsAtUtc, g.Community.TimeZoneId) == CommunityTime.LocalDate(now, g.Community.TimeZoneId),
            position < 0 ? null : position + 1, GatheringRecruitment.Describe(g).Priority == 0,
            wait.Length > 0 && GatheringCapacity.OccupiedSeats(g) >= g.MaximumPlayers,
            GatheringLifecycle.IsUpcoming(g, now) && g.StartsAtUtc <= now.AddHours(2), g.Status == GatheringStatus.Cancelled,
            g.PublicationStatus == GatheringPublicationStatus.Failed, deliveryProblems, provider,
            g.Participants.Where(x => x.Status == GatheringParticipationStatus.Confirmed).Select(x => x.Participant)
                .Prepend(g.OrganizerParticipant).DistinctBy(x => x.Id).Count(x => x.PrivateChatStartedAt is null || x.TelegramDeliveryBlockedAt is not null),
            GatheringLifecycle.IsUpcoming(g, now) ? GatheringRecruitment.Describe(g) : null);
    }
}
