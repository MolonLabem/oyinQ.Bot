namespace oyinQ.Bot.Data.Entities;

public enum RecruitmentDigestState { Pending, Preparing, Delivering, Delivered, Failed, DeliveryUnknown, Expired }

public sealed class RecruitmentDigest
{
    public long Id { get; set; }
    public string CommunityKey { get; set; } = "";
    public OyinQCommunity Community { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; }
    public RecruitmentDigestState State { get; set; }
    public Guid? AttemptId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public int? TelegramMessageId { get; set; }
}
