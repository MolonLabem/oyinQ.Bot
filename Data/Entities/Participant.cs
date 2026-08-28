namespace oyinQ.Bot.Data.Entities;

public sealed class Participant
{
    public long Id { get; set; }
    public long TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PreferredDisplayName { get; set; }
    public string? ActiveCommunityKey { get; set; }
    public int? DaysStaying { get; set; }
    public bool? NeedsAccommodation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<GameCopy> GameCopies { get; set; } = [];
    public ICollection<GameInterest> GameInterests { get; set; } = [];
    public ICollection<GameSession> HostedGameSessions { get; set; } = [];
    public ICollection<GameSessionParticipant> GameSessionParticipations { get; set; } = [];
    public ICollection<GameGathering> OrganizedGatherings { get; set; } = [];
    public ICollection<GameGatheringParticipant> GatheringParticipations { get; set; } = [];
    public ICollection<CampRegistration> CampRegistrations { get; set; } = [];
    public ICollection<CampGameContribution> CampGameContributions { get; set; } = [];
    public ICollection<CollectionImport> CollectionImports { get; set; } = [];
    public ParticipantConversationState? ConversationState { get; set; }
}
