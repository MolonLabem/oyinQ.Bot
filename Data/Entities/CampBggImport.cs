namespace oyinQ.Bot.Data.Entities;

public sealed class CampBggImport
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long CampId { get; set; }
    public long ParticipantId { get; set; }
    public string BggUsername { get; set; } = string.Empty;
    public CampBggImportStatus Status { get; set; }
    public int ProgressCurrent { get; set; }
    public int? ProgressTotal { get; set; }
    public string? DraftJson { get; set; }
    public string? Error { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? CancellationRequestedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public Camp Camp { get; set; } = null!;
    public Participant Participant { get; set; } = null!;
}
