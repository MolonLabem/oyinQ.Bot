using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public static class GatheringPlayTiming
{
    public const int FallbackMinutes = 120;
    public const int ConfirmationGraceMinutes = 30;
    public const int MaximumEstimateMinutes = 7 * 24 * 60;
    public static DateTimeOffset ConfirmationDueAt(GameGathering gathering)
        => EstimatedEnd(gathering).AddMinutes(ConfirmationGraceMinutes);

    public static int EstimateMinutes(GatheringGameSnapshot game) =>
        game.MaxPlayTimeMinutes is > 0 and <= MaximumEstimateMinutes ? game.MaxPlayTimeMinutes.Value
            : game.MinPlayTimeMinutes is > 0 and <= MaximumEstimateMinutes ? game.MinPlayTimeMinutes.Value : FallbackMinutes;

    public static DateTimeOffset EstimatedEnd(GameGathering gathering) => gathering.StartsAtUtc
        .AddMinutes(EstimateMinutes(GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson)));

    public static bool Overlaps(DateTimeOffset firstStart, DateTimeOffset firstEnd, DateTimeOffset secondStart, DateTimeOffset secondEnd)
        => firstStart < secondEnd && secondStart < firstEnd;

    public static int DurationMinutes(DateTimeOffset start, DateTimeOffset end)
    {
        var minutes = Math.Ceiling((end - start).TotalMinutes);
        if (minutes is <= 0 or > int.MaxValue)
            throw new ArgumentException("Окончание партии должно быть позже запланированного начала сбора.");
        return (int)minutes;
    }
}
