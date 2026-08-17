namespace oyinQ.Bot.Data.Entities;

public sealed class GameSessionParticipant
{
    public long Id { get; set; }
    public long GameSessionId { get; set; }
    public long ParticipantId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }

    public GameSession GameSession { get; set; } = null!;
    public Participant Participant { get; set; } = null!;
}
