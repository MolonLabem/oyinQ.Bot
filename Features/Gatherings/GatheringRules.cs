using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Gatherings;

public static class GatheringRules
{
    public const int DescriptionMaxLength = 300;
    public const int CancellationReasonMaxLength = 500;

    public static GameGathering Create(
        string communityKey,
        GatheringGameSnapshot gameSnapshot,
        long organizerParticipantId,
        DateTimeOffset startsAt,
        int minimumPlayers,
        int desiredPlayers,
        int maximumPlayers,
        string? description,
        bool canTeachRules,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(communityKey);
        ArgumentNullException.ThrowIfNull(gameSnapshot);
        GatheringGameSnapshotSerializer.Validate(gameSnapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(organizerParticipantId);
        ValidatePlayerLimits(minimumPlayers, desiredPlayers, maximumPlayers);

        return new GameGathering
        {
            PublicId = Guid.NewGuid(),
            CommunityKey = communityKey.Trim().ToLowerInvariant(),
            GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(gameSnapshot),
            OrganizerParticipantId = organizerParticipantId,
            StartsAtUtc = startsAt.ToUniversalTime(),
            MinimumPlayers = minimumPlayers,
            DesiredPlayers = desiredPlayers,
            MaximumPlayers = maximumPlayers,
            Description = NormalizeDescription(description),
            CanTeachRules = canTeachRules,
            Status = minimumPlayers <= 1 ? GatheringStatus.Ready : GatheringStatus.Recruiting,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime(),
            Expansions = gameSnapshot.SelectedExpansions.Select(value => new GameGatheringExpansion
            {
                BggId = value.BggId,
                Name = value.Name
            }).ToArray()
        };
    }

    public static void UpdatePresentation(
        GameGathering gathering,
        string? description,
        bool canTeachRules,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(gathering);
        EnsureEditable(gathering);
        gathering.Description = NormalizeDescription(description);
        gathering.CanTeachRules = canTeachRules;
        gathering.UpdatedAt = now.ToUniversalTime();
    }

    public static void ValidatePlayerLimits(int minimumPlayers, int desiredPlayers, int maximumPlayers)
    {
        if (minimumPlayers < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPlayers), "Minimum players must include at least the organizer.");
        }

        if (minimumPlayers > desiredPlayers || desiredPlayers > maximumPlayers)
        {
            throw new ArgumentException("Player limits must satisfy minimum <= desired <= maximum.");
        }
    }

    public static string? NormalizeDescription(string? description)
    {
        var normalized = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalized?.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Gathering description cannot exceed {DescriptionMaxLength} characters.",
                nameof(description));
        }

        return normalized;
    }

    private static void EnsureEditable(GameGathering gathering)
    {
        if (gathering.Status is GatheringStatus.Completed or GatheringStatus.Cancelled)
        {
            throw new InvalidOperationException("Completed or cancelled gatherings cannot be edited.");
        }
    }
}
