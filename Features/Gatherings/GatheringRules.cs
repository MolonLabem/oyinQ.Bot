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
        EnsureFutureStart(startsAt, now);
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
            PublicationStatus = GatheringPublicationStatus.Pending,
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
            throw new ArgumentOutOfRangeException(nameof(minimumPlayers), "Минимум игроков должен учитывать организатора.");
        }

        if (minimumPlayers > desiredPlayers || desiredPlayers > maximumPlayers)
        {
            throw new ArgumentException("Лимиты должны соответствовать правилу: минимум ≤ желаемое число ≤ максимум.");
        }
    }

    public static void Update(
        GameGathering gathering,
        DateTimeOffset startsAt,
        int minimumPlayers,
        int desiredPlayers,
        int maximumPlayers,
        string? description,
        bool canTeachRules,
        IReadOnlyCollection<long> selectedExpansionIds,
        DateTimeOffset now)
    {
        EnsureEditable(gathering);
        if (gathering.StartsAtUtc <= now.ToUniversalTime())
            throw new InvalidOperationException("Прошедший сбор нельзя редактировать.");
        EnsureFutureStart(startsAt, now);
        ValidatePlayerLimits(minimumPlayers, desiredPlayers, maximumPlayers);
        var confirmed = 1 + gathering.Participants.Count(x => x.Status == GatheringParticipationStatus.Confirmed);
        if (maximumPlayers < confirmed)
            throw new InvalidOperationException("Максимум игроков не может быть меньше числа подтверждённых участников.");

        var snapshot = GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson);
        var known = snapshot.KnownExpansions ?? snapshot.SelectedExpansions;
        if (selectedExpansionIds.Any(id => known.All(x => x.BggId != id)))
            throw new InvalidOperationException("Выбрано неизвестное дополнение.");
        var selected = known.Where(x => selectedExpansionIds.Contains(x.BggId)).ToArray();
        snapshot = snapshot with { Version = GatheringGameSnapshot.CurrentVersion,
            SelectedExpansions = selected, KnownExpansions = known };
        gathering.GameSnapshotJson = GatheringGameSnapshotSerializer.Serialize(snapshot);
        gathering.Expansions.Clear();
        foreach (var expansion in selected)
            gathering.Expansions.Add(new GameGatheringExpansion { BggId = expansion.BggId, Name = expansion.Name });
        gathering.StartsAtUtc = startsAt.ToUniversalTime();
        gathering.MinimumPlayers = minimumPlayers;
        gathering.DesiredPlayers = desiredPlayers;
        gathering.MaximumPlayers = maximumPlayers;
        gathering.Description = NormalizeDescription(description);
        gathering.CanTeachRules = canTeachRules;
        RecalculateStatus(gathering);
        gathering.UpdatedAt = now.ToUniversalTime();
    }

    public static void Close(GameGathering gathering, DateTimeOffset now)
    {
        EnsureEditable(gathering);
        gathering.Status = GatheringStatus.Closed;
        gathering.UpdatedAt = now.ToUniversalTime();
    }

    public static void EnsureFutureStart(DateTimeOffset startsAt, DateTimeOffset now)
    {
        if (startsAt.ToUniversalTime() <= now.ToUniversalTime())
            throw new InvalidOperationException("Дата и время сбора должны быть в будущем.");
    }

    public static void Reopen(GameGathering gathering, DateTimeOffset now)
    {
        if (gathering.Status != GatheringStatus.Closed)
            throw new InvalidOperationException("Возобновить можно только закрытую запись.");
        if (gathering.StartsAtUtc <= now.ToUniversalTime())
            throw new InvalidOperationException("Нельзя возобновить запись на прошедший сбор.");
        RecalculateStatus(gathering);
        gathering.UpdatedAt = now.ToUniversalTime();
    }

    public static void Cancel(GameGathering gathering, string? reason, DateTimeOffset now)
    {
        if (gathering.Status is GatheringStatus.Completed or GatheringStatus.Cancelled)
            throw new InvalidOperationException("Сбор уже завершён или отменён.");
        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalized?.Length > CancellationReasonMaxLength)
            throw new ArgumentException($"Причина отмены не может быть длиннее {CancellationReasonMaxLength} символов.");
        gathering.Status = GatheringStatus.Cancelled;
        gathering.CancellationReason = normalized;
        gathering.CancelledAt = now.ToUniversalTime();
        gathering.UpdatedAt = now.ToUniversalTime();
    }

    public static string? NormalizeDescription(string? description)
    {
        var normalized = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalized?.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Описание сбора не может быть длиннее {DescriptionMaxLength} символов.",
                nameof(description));
        }

        return normalized;
    }

    private static void EnsureEditable(GameGathering gathering)
    {
        if (gathering.Status is GatheringStatus.Completed or GatheringStatus.Cancelled)
        {
            throw new InvalidOperationException("Завершённый или отменённый сбор нельзя изменить.");
        }
    }

    private static void RecalculateStatus(GameGathering gathering)
    {
        var confirmed = 1 + gathering.Participants.Count(x => x.Status == GatheringParticipationStatus.Confirmed);
        gathering.Status = confirmed >= gathering.MaximumPlayers ? GatheringStatus.Full
            : confirmed >= gathering.MinimumPlayers ? GatheringStatus.Ready : GatheringStatus.Recruiting;
    }
}
