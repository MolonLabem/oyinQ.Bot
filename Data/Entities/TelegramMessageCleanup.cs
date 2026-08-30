namespace oyinQ.Bot.Data.Entities;

public sealed class TelegramMessageCleanup
{
    public long Id { get; set; }
    public long TelegramChatId { get; set; }
    public int TelegramMessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
