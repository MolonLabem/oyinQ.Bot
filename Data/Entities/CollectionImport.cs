namespace oyinQ.Bot.Data.Entities;

public sealed class CollectionImport
{
    public long Id { get; set; }
    public long? ParticipantId { get; set; }
    public long RequestedByTelegramUserId { get; set; }
    public ImportTarget Target { get; set; }
    public ExternalGameProvider Provider { get; set; }
    public string ExternalUsername { get; set; } = string.Empty;
    public ImportStatus Status { get; set; }
    public string? ProgressJson { get; set; }
    public int AddedCount { get; set; }
    public int SkippedCount { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Participant? Participant { get; set; }
}
