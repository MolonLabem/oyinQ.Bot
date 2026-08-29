namespace oyinQ.Bot.Data.Entities;

public sealed class PendingTelegramPeerSelection
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int RequestId { get; set; }
    public long RequestedByTelegramUserId { get; set; }
    public TelegramPeerSelectionPurpose Purpose { get; set; }
    public TelegramPeerSelectionStatus Status { get; set; }
    public string? PreparedButtonId { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
