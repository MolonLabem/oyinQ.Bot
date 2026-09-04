namespace oyinQ.Bot.Data.Entities;

// Interest in a canonical base game, independent of ownership and event commitments.
public sealed class GameWish
{
    public string CommunityKey { get; set; } = "";
    public OyinQCommunity Community { get; set; } = null!;
    public long ParticipantId { get; set; }
    public Participant Participant { get; set; } = null!;
    public long BggId { get; set; }
    public string SnapshotJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
