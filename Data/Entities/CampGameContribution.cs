namespace oyinQ.Bot.Data.Entities;

public sealed class CampGameContribution
{
    public long Id { get; set; }
    public long CampId { get; set; }
    public long ParticipantId { get; set; }
    public long BggId { get; set; }
    public CampContributionItemType ItemType { get; set; }
    public long? ParentBggId { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Camp Camp { get; set; } = null!;
    public Participant Participant { get; set; } = null!;
}
