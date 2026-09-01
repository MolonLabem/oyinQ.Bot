namespace oyinQ.Bot.Data.Entities;

public sealed class KnownTelegramChat
{
    public long TelegramChatId { get; set; }
    public string? Title { get; set; }
    public string? Username { get; set; }
    public bool IsForum { get; set; }
    public bool IsBotPresent { get; set; } = true;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
