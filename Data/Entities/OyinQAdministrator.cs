namespace oyinQ.Bot.Data.Entities;

public sealed class OyinQAdministrator
{
    public long TelegramUserId { get; set; }
    public long? AddedByTelegramUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
