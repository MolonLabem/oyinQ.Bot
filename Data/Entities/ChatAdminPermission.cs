namespace oyinQ.Bot.Data.Entities;

public sealed class ChatAdminPermission
{
    public long Id { get; set; }
    public string CommunityKey { get; set; } = string.Empty;
    public long TelegramUserId { get; set; }
    public string? DisplayName { get; set; }
    public string? TelegramUsername { get; set; }
    public long GrantedByTelegramUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public OyinQCommunity Community { get; set; } = null!;
}
