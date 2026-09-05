namespace oyinQ.Bot.Data.Entities;

public enum GatheringStatus
{
    // Scheduled enrollment states. Capacity owns Recruiting/Ready/Full; Closed is an organizer override.
    Recruiting = 0,
    Ready = 1,
    Full = 2,
    Closed = 3,
    // Terminal lifecycle states. GatheringLifecycle owns these transitions.
    Completed = 4,
    Cancelled = 5
}

public enum GatheringParticipationStatus
{
    Confirmed = 0,
    Waitlisted = 1,
    Withdrawn = 2
}

public enum AttendanceOutcome
{
    Unknown = 0,
    Attended = 1,
    NoShow = 2,
    CancelledInAdvance = 3
}

public enum CampStatus
{
    Draft = 0,
    Active = 1,
    Closed = 2,
    Cancelled = 3
}

public enum CollectionItemType
{
    BaseGame = 0,
    Expansion = 1
}

public enum CollectionItemSource
{
    Legacy = 0,
    BggImport = 1,
    Manual = 2
}

public enum CampBringCommitment
{
    Available = 0,
    Bringing = 1
}

public enum CampImportSkipReason
{
    AlreadyInBaseCollection = 0,
    AlreadyAddedManually = 1,
    InvalidOrUnsupportedItem = 2,
    ProviderDataIncomplete = 3
}

public enum CampImportOverrideResolution
{
    KeepBaseCollection = 0,
    AddPersonalCopies = 1
}

public enum ClubMetadataRefreshStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

public enum ClubBggImportStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

public enum CampBggImportStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Confirmed = 3,
    Failed = 4,
    Cancelled = 5
}

public enum BggImportStage
{
    Queued = 0,
    FetchingGames = 1,
    FetchingExpansions = 2,
    Preparing = 3,
    Saving = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7
}

public enum TelegramPeerSelectionPurpose
{
    AddAdministrator = 0,
    CreateClubChat = 1,
    CreateCampChat = 2
}

public enum TelegramPeerSelectionStatus
{
    Pending = 0,
    Completed = 1,
    Consumed = 2,
    Expired = 3
}

public enum GatheringPublicationStatus
{
    Pending = 0,
    Published = 1,
    Failed = 2
}
