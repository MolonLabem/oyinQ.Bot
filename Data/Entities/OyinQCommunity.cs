using oyinQ.Bot.Common.Options;

namespace oyinQ.Bot.Data.Entities;

public sealed class OyinQCommunity
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long TelegramChatId { get; set; }
    public BotMode Mode { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Club? Club { get; set; }
    public Camp? Camp { get; set; }
    public ICollection<GameGathering> Gatherings { get; set; } = [];
    public ICollection<ChatAdminPermission> AdminPermissions { get; set; } = [];

    public BotCommunity ToBotCommunity() =>
        new(Key, Name, TelegramChatId, Mode, TimeZoneId);
}
