namespace oyinQ.Bot.Data.Entities;

public sealed class TelegramForumTopic
{
    public long TelegramChatId { get; set; }
    public int MessageThreadId { get; set; }
    public string? Name { get; set; }
    public bool IsClosed { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
