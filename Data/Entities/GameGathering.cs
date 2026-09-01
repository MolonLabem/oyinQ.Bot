namespace oyinQ.Bot.Data.Entities;

public sealed class GameGathering
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public string CommunityKey { get; set; } = string.Empty;
    public string GameSnapshotJson { get; set; } = string.Empty;
    public long OrganizerParticipantId { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public int MinimumPlayers { get; set; }
    public int DesiredPlayers { get; set; }
    public int MaximumPlayers { get; set; }
    public string? Description { get; set; }
    public bool CanTeachRules { get; set; }
    public GatheringStatus Status { get; set; }
    public long? TelegramChatId { get; set; }
    public int? TelegramMessageId { get; set; }
    public GatheringPublicationStatus PublicationStatus { get; set; }
    public string? PublicationError { get; set; }
    public int PublicationAttempts { get; set; }
    public DateTimeOffset? LastPublicationAttemptAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public OyinQCommunity Community { get; set; } = null!;
    public Participant OrganizerParticipant { get; set; } = null!;
    public ICollection<GameGatheringExpansion> Expansions { get; set; } = [];
    public ICollection<GameGatheringParticipant> Participants { get; set; } = [];
}
