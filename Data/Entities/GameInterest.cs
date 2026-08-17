namespace oyinQ.Bot.Data.Entities;

public sealed class GameInterest
{
    public long Id { get; set; }
    public long ParticipantId { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Participant Participant { get; set; } = null!;
    public Game Game { get; set; } = null!;
}
