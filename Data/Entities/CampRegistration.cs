namespace oyinQ.Bot.Data.Entities;

public sealed class CampRegistration
{
    public long Id { get; set; }
    public long CampId { get; set; }
    public long ParticipantId { get; set; }
    public int? DaysStaying { get; set; }
    public bool? NeedsAccommodation { get; set; }
    public string? City { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Camp Camp { get; set; } = null!;
    public Participant Participant { get; set; } = null!;
}
