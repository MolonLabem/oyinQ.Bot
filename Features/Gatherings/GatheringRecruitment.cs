using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record RecruitmentState(int Priority, string Text, int FreeSeats, bool BelowDesired);

public static class GatheringRecruitment
{
    public static RecruitmentState Describe(GameGathering g)
    {
        var occupied = GatheringCapacity.OccupiedSeats(g);
        var free = Math.Max(0, g.MaximumPlayers - occupied);
        var minimum = g.MinimumPlayers - occupied;
        var desired = g.DesiredPlayers - occupied;
        return minimum > 0 ? new(0, $"🔴 {(minimum == 1 ? "Нужен" : "Нужны")} +{minimum}, чтобы игра состоялась", free, true)
            : desired > 0 ? new(1, $"🟡 Состав есть · +{desired} до оптимального", free, true)
            : free > 0 ? new(2, $"🟢 Можно ещё +{free}", free, false)
            : new(3, "Состав набран", 0, false);
    }

    // One expression supplies both SQL candidate selection and in-memory eligibility.
    public static Expression<Func<GameGathering, bool>> CandidatePredicate(DateTimeOffset now, bool includeAllUpcoming = false) =>
        g => g.StartsAtUtc > now && (includeAllUpcoming || g.StartsAtUtc <= now.AddHours(36))
            && (g.Status == GatheringStatus.Recruiting || g.Status == GatheringStatus.Ready);

    public static Task<GameGathering[]> LoadCandidatesAsync(AppDbContext db, string key, DateTimeOffset now,
        bool includeAllUpcoming, CancellationToken ct) =>
        db.GameGatherings.AsNoTracking().Include(x => x.Participants).Include(x => x.Guests)
            .Where(x => x.CommunityKey == key).Where(CandidatePredicate(now, includeAllUpcoming)).ToArrayAsync(ct);

    public static bool IsRelevant(GameGathering g, DateTimeOffset now, bool includeAllUpcoming = false) =>
        CandidatePredicate(now, includeAllUpcoming).Compile()(g) && Describe(g).FreeSeats > 0;

    public static bool CanRequest(GameGathering g, long participantId, DateTimeOffset now) =>
        g.OrganizerParticipantId == participantId && IsRelevant(g, now) && Describe(g).BelowDesired;

    public static IReadOnlyList<GameGathering> Rank(IEnumerable<GameGathering> values, DateTimeOffset now, bool includeAllUpcoming = false) =>
        values.Where(CandidatePredicate(now, includeAllUpcoming).Compile()).Where(g => Describe(g).FreeSeats > 0).OrderBy(g => Describe(g).Priority)
            .ThenBy(g => g.StartsAtUtc).ThenBy(g => g.Id).ToArray();
}
