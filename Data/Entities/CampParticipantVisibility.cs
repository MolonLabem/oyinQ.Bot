namespace oyinQ.Bot.Data.Entities;

// Independent of registration so leaving and rejoining cannot erase a privacy choice.
// An absent row means both collection and wish authorship are visible in this Camp.
public sealed class CampParticipantVisibility
{
    public long CampId { get; set; }
    public long ParticipantId { get; set; }
    public bool ShareCollection { get; set; } = true;
    public bool ShareWishes { get; set; } = true;
}
