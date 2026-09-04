namespace oyinQ.Bot.Data.Entities;

public enum ReleaseDeliveryState { Pending, Delivering, Delivered, Failed, DeliveryUnknown, Preparing }
public sealed class ReleaseAnnouncement
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public long CreatedByParticipantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class ReleaseAnnouncementDelivery
{
    public string ReleaseId { get; set; } = string.Empty;
    public ReleaseAnnouncement Release { get; set; } = null!;
    public string CommunityKey { get; set; } = string.Empty;
    public OyinQCommunity Community { get; set; } = null!;
    public ReleaseDeliveryState State { get; set; }
    public int? TelegramMessageId { get; set; }
    public DateTimeOffset? AttemptedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? Error { get; set; }
}
