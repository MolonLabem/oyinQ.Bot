namespace oyinQ.Bot.Data.Entities;

public sealed class OyinQAdministrator
{
    public long TelegramUserId { get; set; }
    public long? AddedByTelegramUserId { get; set; }
    public string? DisplayName { get; set; }
    public string? TelegramUsername { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
