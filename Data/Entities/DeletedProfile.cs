namespace oyinQ.Bot.Data.Entities;

// Only the opt-out identity survives erasure, so opening an old session cannot recreate a profile.
public sealed class DeletedProfile
{
    public long TelegramUserId { get; set; }
    public DateTimeOffset DeletedAt { get; set; }
}
