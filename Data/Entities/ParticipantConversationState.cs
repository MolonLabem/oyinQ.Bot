namespace oyinQ.Bot.Data.Entities;

public sealed class ParticipantConversationState
{
    public long Id { get; set; }
    public long ParticipantId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Participant Participant { get; set; } = null!;
}
