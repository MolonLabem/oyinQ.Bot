namespace oyinQ.Bot.Data.Entities;

public enum NotificationKind
{
    WaitlistPromotion, GatheringTimeChanged, GatheringCancelled, GatheringFailed,
    GatheringDetailsChanged, GatheringFull, OrganizerParticipantLeft, OrganizerReplacement,
    OrganizerBelowMinimum, OrganizerMissingProvider, ImportCompleted, Reminder, PostingTopicUnavailable, WishlistGathering
}
public enum NotificationState { Pending, Delivered, Failed, SuppressedByPreference, CannotMessageUser, Delivering, DeliveryUnknown, Expired }

public sealed class Notification
{
    public long Id { get; set; }
    public long ParticipantId { get; set; }
    public Participant Participant { get; set; } = null!;
    public string DeduplicationKey { get; set; } = "";
    public NotificationKind Kind { get; set; }
    public NotificationState State { get; set; }
    public Guid? GatheringPublicId { get; set; }
    public string? CommunityKey { get; set; }
    public string Text { get; set; } = "";
    public Guid? ImportPublicId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastErrorCategory { get; set; }
    public int? TelegramMessageId { get; set; }
}

public sealed class NotificationPreferences
{
    public long ParticipantId { get; set; }
    public Participant Participant { get; set; } = null!;
    public bool WishlistGathering { get; set; } = true;
    public bool GatheringFull { get; set; }
    public bool GatheringDetailsChanged { get; set; } = true;
    public bool OrganizerParticipantLeft { get; set; } = true;
    public bool OrganizerReplacement { get; set; } = true;
    public bool OrganizerBelowMinimum { get; set; } = true;
    public bool OrganizerMissingProvider { get; set; }
    public bool ImportCompleted { get; set; } = true;
    public int ReminderLeadMinutes { get; set; }
}
