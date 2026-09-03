namespace oyinQ.Bot.Data.Entities;

public sealed class GameGatheringGuest
{
    public long Id { get; set; }
    public long GameGatheringId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public long CreatedByParticipantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public GameGathering GameGathering { get; set; } = null!;
    public Participant CreatedByParticipant { get; set; } = null!;
}
