namespace oyinQ.Bot.Data.Entities;

public enum ExternalPlayProvider { BgStats }
public sealed class GatheringExternalPlayReference
{
    public const int MaxUrlLength = 2048;
    public long Id { get; set; }
    public long GatheringPlayRecordId { get; set; }
    public GatheringPlayRecord PlayRecord { get; set; } = null!;
    public ExternalPlayProvider Provider { get; set; }
    public string Url { get; set; } = string.Empty;
    public long AddedByParticipantId { get; set; }
    public Participant AddedByParticipant { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
