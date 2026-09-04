namespace oyinQ.Bot.Data.Entities;

public sealed class GatheringPlayRecord
{
    public const int MaxLocationLength = 160;
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long GatheringId { get; set; }
    public GameGathering Gathering { get; set; } = null!;
    public bool WasPlayed { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public int? DurationMinutes { get; set; }
    public long RecordedByParticipantId { get; set; }
    public Participant RecordedByParticipant { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Revision { get; set; }
    public string Location { get; set; } = string.Empty;
    public bool HigherScoreWins { get; set; } = true;
    public string GameSnapshotJson { get; set; } = string.Empty;
    public ICollection<GatheringPlayPlayer> Players { get; set; } = [];
}

public sealed class GatheringPlayPlayer
{
    public long Id { get; set; }
    public long PlayRecordId { get; set; }
    public GatheringPlayRecord PlayRecord { get; set; } = null!;
    public Guid SourcePlayerId { get; set; }
    public long? ParticipantId { get; set; }
    public Participant? Participant { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal? Score { get; set; }
    public bool IsWinner { get; set; }
}
