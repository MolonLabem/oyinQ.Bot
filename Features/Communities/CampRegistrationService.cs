using System.Data;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.Communities;

public sealed record CampRegistrationGatheringImpact(Guid PublicId, string GameName, DateTimeOffset StartsAtUtc);
public sealed record CampRegistrationMutationResult(IReadOnlyList<GatheringWithdrawalOutcome> Withdrawals)
{
    public IReadOnlyList<Guid> CancelledGatheringIds { get; init; } = [];
    public IReadOnlyList<Guid> ChangedGatheringIds => Withdrawals.Select(x => x.GatheringPublicId)
        .Concat(CancelledGatheringIds).Distinct().ToArray();
}

public sealed class CampRegistrationConflictException(
    string message, IReadOnlyList<CampRegistrationGatheringImpact> gatherings,
    bool canConfirm = false) : InvalidOperationException(message)
{
    public IReadOnlyList<CampRegistrationGatheringImpact> Gatherings { get; } = gatherings;
    public bool CanConfirm { get; } = canConfirm;
}

public sealed class CampRegistrationService(AppDbContext dbContext, TimeProvider timeProvider, GatheringNotificationService? notificationService = null)
{
    private readonly GatheringNotificationService notifications = notificationService ?? new(dbContext, new Features.Notifications.NotificationService(dbContext, timeProvider));
    public async Task<CampRegistrationMutationResult> SaveAsync(long campId, long participantId,
        IReadOnlyCollection<DateOnly> selectedDates, bool needsAccommodation, string? displayName, string city,
        bool confirmAttendanceChanges, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? answers = null)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var campQuery = IsNpgsql
            ? dbContext.Camps.FromSqlInterpolated(
                $"SELECT * FROM \"Camps\" WHERE \"Id\" = {campId} FOR UPDATE")
            : dbContext.Camps.Where(x => x.Id == campId);
        var camp = await campQuery
            .Include(x => x.BotChat).SingleAsync(cancellationToken);
        await ParticipantWriteStore.RequireActiveAsync(dbContext, participantId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        CampParticipationPolicy.EnsureAcceptsMutations(camp, camp.BotChat.TimeZoneId, now);
        if (camp.StartDate is not { } start || camp.EndDate is not { } end)
            throw new InvalidOperationException("Для кэмпа ещё не настроены даты.");
        var normalizedDates = CampRules.ValidateSelectedDates(selectedDates, start, end);
        var normalizedCity = CampRules.NormalizeCity(city);
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        var registration = await dbContext.CampRegistrations
            .Include(x => x.SelectedDays)
            .SingleOrDefaultAsync(x => x.CampId == campId && x.ParticipantId == participantId,
                cancellationToken);
        var configuration = CampConfigurationRules.Read(camp.ConfigurationJson);
        var savedData = CampConfigurationRules.ReadRegistration(registration?.RegistrationDataJson);
        var normalizedAnswers = CampConfigurationRules.ValidateAnswers(configuration, answers ?? savedData.Answers);
        var quote = CampConfigurationRules.QuoteForRegistration(configuration, normalizedAnswers, normalizedDates,
            camp.BotChat.TimeZoneId, now, registration, needsAccommodation);
        var removed = registration is null ? [] : registration.SelectedDays.Select(x => x.Date)
            .Except(normalizedDates).ToHashSet();
        var impacted = removed.Count == 0 ? [] : await LoadImpactedGatheringsAsync(
            camp.BotChatKey, participantId, removed, now, camp.BotChat.TimeZoneId, cancellationToken);
        var organized = impacted.Where(x => x.IsOrganizer).Select(x => x.Impact).ToArray();
        if (organized.Length > 0)
            throw new CampRegistrationConflictException(
                "Вы организуете сборы в удаляемые дни. Сначала отмените эти сборы.", organized);
        var participantImpacts = impacted.Where(x => !x.IsOrganizer).ToArray();
        if (participantImpacts.Length > 0 && !confirmAttendanceChanges)
            throw new CampRegistrationConflictException(
                "После сохранения вы выйдете из будущих сборов в удаляемые дни.",
                participantImpacts.Select(x => x.Impact).ToArray(), canConfirm: true);

        registration ??= new CampRegistration
        {
            CampId = campId, ParticipantId = participantId, CreatedAt = now
        };
        if (registration.Id == 0) dbContext.CampRegistrations.Add(registration);
        registration.DisplayName = normalizedDisplayName;
        registration.RegistrationDataJson = CampConfigurationRules.Serialize(new CampRegistrationData(Answers: normalizedAnswers, Quote: quote));
        registration.City = normalizedCity;
        registration.NeedsAccommodation = needsAccommodation;
        registration.DaysStaying = normalizedDates.Count;
        registration.UpdatedAt = now;
        registration.SelectedDays.Clear();
        foreach (var date in normalizedDates)
            registration.SelectedDays.Add(new CampRegistrationDay { Date = date });

        var withdrawals = new List<GatheringWithdrawalOutcome>();
        foreach (var value in participantImpacts)
        {
            var withdrawal = GatheringRules.WithdrawParticipant(value.Gathering, value.Membership!, now)!;
            withdrawals.Add(GatheringWithdrawalOutcome.Capture(value.Gathering, withdrawal));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var withdrawal in withdrawals) await notifications.NotifyWithdrawalAsync(withdrawal, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(withdrawals);
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > 128)
            throw new ArgumentException("Имя не должно быть длиннее 128 символов.");
        return normalized;
    }

    public async Task<CampRegistrationMutationResult> UnregisterAsync(long campId, long participantId,
        CancellationToken cancellationToken)
    {
        // The Camp lock serializes creation and registration mutations. Read a fresh
        // snapshot after waiting so a creation committed first is included below.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var campQuery = IsNpgsql
            ? dbContext.Camps.FromSqlInterpolated($"SELECT * FROM \"Camps\" WHERE \"Id\" = {campId} FOR UPDATE")
            : dbContext.Camps.Where(x => x.Id == campId);
        var camp = await campQuery.Include(x => x.BotChat).SingleAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var registration = await dbContext.CampRegistrations.Include(x => x.SelectedDays)
            .SingleOrDefaultAsync(x => x.CampId == campId && x.ParticipantId == participantId,
                cancellationToken);
        if (registration is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new([]);
        }
        var references = await dbContext.GameGatherings
            .Where(x => x.CommunityKey == camp.BotChatKey && x.StartsAtUtc > now
                && GatheringLifecycle.ScheduledStatuses.Contains(x.Status)
                && (x.OrganizerParticipantId == participantId
                    || x.Participants.Any(p => p.ParticipantId == participantId
                        && (p.Status == GatheringParticipationStatus.Confirmed
                            || p.Status == GatheringParticipationStatus.Waitlisted))))
            .OrderBy(x => x.Id).Select(x => x.PublicId)
            .ToArrayAsync(cancellationToken);
        var withdrawals = new List<GatheringWithdrawalOutcome>();
        var cancelled = new List<Guid>();
        foreach (var publicId in references)
        {
            var gathering = await GatheringWriteStore.LockAsync(dbContext, publicId, camp.BotChatKey, cancellationToken);
            now = timeProvider.GetUtcNow();
            if (!GatheringLifecycle.IsUpcoming(gathering, now)) continue;
            if (gathering.OrganizerParticipantId == participantId)
            {
                GatheringRules.Cancel(gathering, "Организатор отменил регистрацию на кэмп", now);
                GatheringPublication.Request(gathering);
                cancelled.Add(publicId);
            }
            else if (gathering.Participants.SingleOrDefault(x => x.ParticipantId == participantId) is { } membership
                && GatheringRules.WithdrawParticipant(gathering, membership, now) is { } withdrawal)
                withdrawals.Add(GatheringWithdrawalOutcome.Capture(gathering, withdrawal));
        }
        await RemoveContributionsAndCancelImportsAsync(campId, participantId, now, cancellationToken);
        dbContext.CampRegistrations.Remove(registration);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var withdrawal in withdrawals) await notifications.NotifyWithdrawalAsync(withdrawal, cancellationToken);
        foreach (var publicId in cancelled) await notifications.NotifyCancellationAsync(publicId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(withdrawals) { CancelledGatheringIds = cancelled };
    }

    private async Task<IReadOnlyList<ImpactedGathering>> LoadImpactedGatheringsAsync(string communityKey,
        long participantId, IReadOnlySet<DateOnly> removedDates, DateTimeOffset now, string timeZoneId,
        CancellationToken cancellationToken)
    {
        var values = await dbContext.GameGatherings
            .Where(x => x.CommunityKey == communityKey && x.StartsAtUtc > now
                && GatheringLifecycle.ScheduledStatuses.Contains(x.Status)
                && (x.OrganizerParticipantId == participantId
                    || x.Participants.Any(p => p.ParticipantId == participantId
                        && (p.Status == GatheringParticipationStatus.Confirmed
                            || p.Status == GatheringParticipationStatus.Waitlisted))))
            .Include(x => x.Participants).ThenInclude(x => x.Participant)
            .Include(x => x.OrganizerParticipant)
            .Include(x => x.Guests)
            .ToArrayAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return values.Where(x => removedDates.Contains(DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(x.StartsAtUtc, zone).DateTime)))
            .Select(x => new ImpactedGathering(x, x.OrganizerParticipantId == participantId,
                x.Participants.SingleOrDefault(p => p.ParticipantId == participantId), ToImpact(x)))
            .ToArray();
    }

    private static CampRegistrationGatheringImpact ToImpact(GameGathering gathering) => new(
        gathering.PublicId,
        GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson).Name,
        gathering.StartsAtUtc);

    private bool IsNpgsql => string.Equals(dbContext.Database.ProviderName,
        "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);

    private async Task RemoveContributionsAndCancelImportsAsync(long campId, long participantId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var contributions = dbContext.CampGameContributions.Where(x => x.CampId == campId
            && x.ParticipantId == participantId);
        var imports = dbContext.CampBggImports.Where(x => x.CampId == campId && x.ParticipantId == participantId
            && (x.Status == CampBggImportStatus.Queued || x.Status == CampBggImportStatus.Running
                || x.Status == CampBggImportStatus.Completed));
        if (IsNpgsql)
        {
            await contributions.ExecuteDeleteAsync(cancellationToken);
            await imports.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, CampBggImportStatus.Cancelled)
                .SetProperty(x => x.Stage, BggImportStage.Cancelled)
                .SetProperty(x => x.CancellationRequestedAt, now)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
            return;
        }

        dbContext.CampGameContributions.RemoveRange(await contributions.ToArrayAsync(cancellationToken));
        foreach (var import in await imports.ToArrayAsync(cancellationToken))
        {
            import.Status = CampBggImportStatus.Cancelled;
            import.Stage = BggImportStage.Cancelled;
            import.CancellationRequestedAt = now;
            import.UpdatedAt = now;
        }
    }

    private sealed record ImpactedGathering(GameGathering Gathering, bool IsOrganizer,
        GameGatheringParticipant? Membership, CampRegistrationGatheringImpact Impact);
}
