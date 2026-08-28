namespace oyinQ.Bot.Data.Entities;

public sealed class GameGatheringParticipant
{
    public long Id { get; set; }
    public long GameGatheringId { get; set; }
    public long ParticipantId { get; set; }
    public GatheringParticipationStatus Status { get; set; }
    public AttendanceOutcome AttendanceOutcome { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }

    public GameGathering GameGathering { get; set; } = null!;
    public Participant Participant { get; set; } = null!;
}
