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

    public static bool IsRelevant(GameGathering g, DateTimeOffset now) =>
        g.StartsAtUtc > now && g.StartsAtUtc <= now.AddHours(36)
        && g.Status is GatheringStatus.Recruiting or GatheringStatus.Ready
        && Describe(g).FreeSeats > 0;

    public static bool CanRequest(GameGathering g, long participantId, DateTimeOffset now) =>
        g.OrganizerParticipantId == participantId && IsRelevant(g, now) && Describe(g).BelowDesired;

    public static IReadOnlyList<GameGathering> Rank(IEnumerable<GameGathering> values, DateTimeOffset now) =>
        values.Where(g => IsRelevant(g, now)).OrderBy(g => Describe(g).Priority)
            .ThenBy(g => g.StartsAtUtc).ThenBy(g => g.Id).ToArray();
}
