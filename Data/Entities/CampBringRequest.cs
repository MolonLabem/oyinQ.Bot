namespace oyinQ.Bot.Data.Entities;

// One durable conversation per Camp/owner/game. A refusal cannot be bypassed by a new requester.
public sealed class CampBringRequest
{
    public long Id { get; set; }
    public long CampId { get; set; }
    public Camp Camp { get; set; } = null!;
    public long OwnerParticipantId { get; set; }
    public Participant Owner { get; set; } = null!;
    public long BggId { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public bool Declined { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<CampBringRequester> Requesters { get; set; } = [];
}

public sealed class CampBringRequester
{
    public long RequestId { get; set; }
    public CampBringRequest Request { get; set; } = null!;
    public long ParticipantId { get; set; }
    public Participant Participant { get; set; } = null!;
    public DateOnly[] Dates { get; set; } = [];
    public bool Active { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
