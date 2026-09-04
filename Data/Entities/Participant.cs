namespace oyinQ.Bot.Data.Entities;

public sealed class Participant
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PreferredDisplayName { get; set; }
    public string? ActiveCommunityKey { get; set; }
    public DateTimeOffset? TelegramDeliveryBlockedAt { get; set; }
    public DateTimeOffset? PrivateChatStartedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<GameGathering> OrganizedGatherings { get; set; } = [];
    public ICollection<GameGatheringParticipant> GatheringParticipations { get; set; } = [];
    public ICollection<GameGatheringGuest> CreatedGatheringGuests { get; set; } = [];
    public ICollection<CampRegistration> CampRegistrations { get; set; } = [];
    public ICollection<CampGameContribution> CampGameContributions { get; set; } = [];
}
