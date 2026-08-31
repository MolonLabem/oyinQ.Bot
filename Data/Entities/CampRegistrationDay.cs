namespace oyinQ.Bot.Data.Entities;

public sealed class CampRegistrationDay
{
    public long CampRegistrationId { get; set; }
    public DateOnly Date { get; set; }

    public CampRegistration CampRegistration { get; set; } = null!;
}
