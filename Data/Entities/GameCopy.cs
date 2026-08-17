namespace oyinQ.Bot.Data.Entities;

public sealed class GameCopy
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public long? OwnerParticipantId { get; set; }
    public GameCopySource Source { get; set; }
    public BringStatus BringStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Game Game { get; set; } = null!;
    public Participant? OwnerParticipant { get; set; }
}
