namespace oyinQ.Bot.Data.Entities;

public sealed class GameSession
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public long HostParticipantId { get; set; }
    public long? TelegramChatId { get; set; }
    public int? TelegramMessageId { get; set; }
    public int WantedAdditionalPlayers { get; set; }
    public SessionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public Game Game { get; set; } = null!;
    public Participant HostParticipant { get; set; } = null!;
    public ICollection<GameSessionParticipant> Participants { get; set; } = [];
}
