using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.Collections;

public sealed record CampBggImportView(
    Guid PublicId,
    CampBggImportStatus Status,
    string BggUsername,
    int ProgressCurrent,
    int? ProgressTotal,
    CampBggImportDraft? Draft,
    string? Error,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    CampImportOverrideResolution? OverrideResolution,
    bool HasSelectedOverridableItems);
public sealed record CampImportConfirmationResult(int Added, IReadOnlyDictionary<CampImportSkipReason, int> Skipped,
    bool HasOverridableItems, bool WasAlreadyConfirmed = false);

public sealed class CampBggImportCoordinator(
    AppDbContext dbContext,
    CampContributionSelectionService contributionService,
    CampParticipationPolicy participationPolicy,
    TimeProvider timeProvider)
{
    public async Task<CampBggImport> QueueAsync(long campId, long participantId, string username,
        CancellationToken cancellationToken)
    {
        await participationPolicy.RequireCompleteRegistrationAsync(campId, participantId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        await dbContext.CampBggImports
            .Where(x => x.CampId == campId && x.ParticipantId == participantId
                && (x.Status == CampBggImportStatus.Queued || x.Status == CampBggImportStatus.Running)
                && x.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, CampBggImportStatus.Cancelled)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        var active = await dbContext.CampBggImports.AsNoTracking()
            .Where(x => x.CampId == campId && x.ParticipantId == participantId
                && (x.Status == CampBggImportStatus.Queued || x.Status == CampBggImportStatus.Running))
            .OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (active is not null) return active;
        var import = new CampBggImport
        {
            PublicId = Guid.NewGuid(), CampId = campId, ParticipantId = participantId,
            BggUsername = username, Status = CampBggImportStatus.Queued,
            CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddDays(7)
        };
        dbContext.CampBggImports.Add(import);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return import;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(import).State = EntityState.Detached;
            return await dbContext.CampBggImports.AsNoTracking()
                .Where(x => x.CampId == campId && x.ParticipantId == participantId
                    && (x.Status == CampBggImportStatus.Queued || x.Status == CampBggImportStatus.Running))
                .OrderBy(x => x.CreatedAt).FirstAsync(cancellationToken);
        }
    }

    public async Task<CampBggImportView> GetAsync(Guid publicId, long campId, long participantId,
        CancellationToken cancellationToken)
    {
        var import = await RequireOwnedAsync(publicId, campId, participantId, cancellationToken);
        return new CampBggImportView(import.PublicId, import.Status, import.BggUsername,
            import.ProgressCurrent, import.ProgressTotal,
            import.Status is CampBggImportStatus.Completed or CampBggImportStatus.Confirmed
                ? CampBggImportDraftSerializer.Deserialize(import.DraftJson) : null,
            import.Error, import.UpdatedAt, import.ExpiresAt, import.OverrideResolution,
            import.ConfirmationJson is not null
                && CampBggImportConfirmationSerializer.Deserialize(import.ConfirmationJson)
                    .SelectedOverridableItems.Count > 0);
    }

    public async Task<CampImportConfirmationResult> ConfirmAsync(Guid publicId, long campId, long participantId,
        IReadOnlyCollection<long> selectedBaseGameIds, IReadOnlyCollection<long> selectedExpansionIds,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var import = await dbContext.CampBggImports
            .FromSqlInterpolated($"SELECT * FROM \"CampBggImports\" WHERE \"PublicId\" = {publicId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Импорт не найден.");
        EnsureOwner(import, campId, participantId);
        if (import.ExpiresAt <= timeProvider.GetUtcNow()) throw new InvalidOperationException("Черновик импорта истёк.");
        if (import.Status == CampBggImportStatus.Confirmed)
        {
            var confirmedDraft = CampBggImportDraftSerializer.Deserialize(import.DraftJson);
            var replayConfirmation = import.ConfirmationJson is null
                ? await RecoverConfirmationAsync(import, confirmedDraft, cancellationToken)
                : CampBggImportConfirmationSerializer.Deserialize(import.ConfirmationJson);
            if (import.ConfirmationJson is null)
            {
                import.ConfirmationJson = CampBggImportConfirmationSerializer.Serialize(replayConfirmation);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return Result(replayConfirmation) with { WasAlreadyConfirmed = true };
        }
        if (import.Status != CampBggImportStatus.Completed) throw new InvalidOperationException("Импорт ещё не готов.");
        var draft = CampBggImportDraftSerializer.Deserialize(import.DraftJson);
        await contributionService.ConfirmImportAsync(campId, participantId, draft,
            selectedBaseGameIds, selectedExpansionIds, timeProvider.GetUtcNow(), cancellationToken);
        var confirmation = BuildConfirmation(draft, selectedBaseGameIds, selectedExpansionIds);
        import.Status = CampBggImportStatus.Confirmed;
        import.ConfirmationJson = CampBggImportConfirmationSerializer.Serialize(confirmation);
        import.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result(confirmation);
    }

    public async Task ResolveBaseDuplicatesAsync(Guid publicId, long campId, long participantId,
        CampImportOverrideResolution resolution, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var import = await dbContext.CampBggImports
            .FromSqlInterpolated($"SELECT * FROM \"CampBggImports\" WHERE \"PublicId\" = {publicId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Импорт не найден.");
        EnsureOwner(import, campId, participantId);
        if (import.Status != CampBggImportStatus.Confirmed) throw new InvalidOperationException("Сначала подтвердите импорт.");
        if (import.OverrideResolution is not null) { await transaction.CommitAsync(cancellationToken); return; }
        await participationPolicy.RequireCompleteRegistrationAsync(campId, participantId, cancellationToken);
        var draft = CampBggImportDraftSerializer.Deserialize(import.DraftJson);
        var confirmation = import.ConfirmationJson is null
            ? await RecoverConfirmationAsync(import, draft, cancellationToken)
            : CampBggImportConfirmationSerializer.Deserialize(import.ConfirmationJson);
        if (confirmation.SelectedOverridableItems.Count == 0)
            throw new InvalidOperationException("В импорте нет копий, которые можно добавить.");
        if (resolution == CampImportOverrideResolution.AddPersonalCopies)
            await contributionService.AddSoftSkippedCopiesAsync(campId, participantId, draft,
                confirmation.SelectedOverridableItems,
                timeProvider.GetUtcNow(), cancellationToken);
        import.OverrideResolution = resolution;
        import.OverrideResolvedAt = timeProvider.GetUtcNow();
        import.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResolveBaseDuplicatesFromTelegramAsync(Guid publicId, long telegramUserId,
        CampImportOverrideResolution resolution, CancellationToken cancellationToken)
    {
        var owned = await dbContext.CampBggImports.AsNoTracking()
            .Where(x => x.PublicId == publicId && x.Participant.TelegramUserId == telegramUserId)
            .Select(x => new { x.CampId, x.ParticipantId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("Импорт принадлежит другому участнику.");
        await ResolveBaseDuplicatesAsync(publicId, owned.CampId, owned.ParticipantId, resolution,
            cancellationToken);
    }

    public static CampBggImportConfirmation BuildConfirmation(CampBggImportDraft draft,
        IReadOnlyCollection<long> selectedBaseIds, IReadOnlyCollection<long> selectedExpansionIds)
    {
        var selectedBase = selectedBaseIds.ToHashSet(); var selectedExpansions = selectedExpansionIds.ToHashSet();
        var added = draft.Items.Count(x => x.SkipReason is null && (x.ItemType == CampContributionItemType.BaseGame
            ? selectedBase.Contains(x.BggId) : selectedExpansions.Contains(x.BggId)));
        var skipped = draft.Items.Where(x => x.SkipReason.HasValue).GroupBy(x => x.SkipReason!.Value)
            .ToDictionary(x => x.Key, x => x.Count());
        var overridable = draft.Items.Where(x => x.SkipReason == CampImportSkipReason.AlreadyInBaseCollection
                && x.IsOverridable && (x.ItemType == CampContributionItemType.BaseGame
                    ? selectedBase.Contains(x.BggId) : selectedExpansions.Contains(x.BggId)))
            .Select(x => new CampImportItemKey(x.BggId, x.ItemType)).ToArray();
        return new CampBggImportConfirmation(CampBggImportConfirmation.CurrentVersion,
            selectedBase.Order().ToArray(), selectedExpansions.Order().ToArray(), overridable, added, skipped);
    }

    private static CampImportConfirmationResult Result(CampBggImportConfirmation confirmation) =>
        new(confirmation.Added, confirmation.Skipped, confirmation.SelectedOverridableItems.Count > 0);

    private async Task<CampBggImportConfirmation> RecoverConfirmationAsync(CampBggImport import,
        CampBggImportDraft draft, CancellationToken cancellationToken)
    {
        var applied = await dbContext.CampGameContributions.AsNoTracking()
            .Where(x => x.CampId == import.CampId && x.ParticipantId == import.ParticipantId
                && x.Source == CampContributionSource.BggImport)
            .Select(x => new { x.BggId, x.ItemType }).ToArrayAsync(cancellationToken);
        var keys = applied.Select(x => (x.BggId, x.ItemType)).ToHashSet();
        var selectedBase = draft.Items.Where(x => x.ItemType == CampContributionItemType.BaseGame
                && x.SkipReason is null && keys.Contains((x.BggId, x.ItemType)))
            .Select(x => x.BggId).ToArray();
        var selectedExpansions = draft.Items.Where(x => x.ItemType == CampContributionItemType.Expansion
                && x.SkipReason is null && keys.Contains((x.BggId, x.ItemType)))
            .Select(x => x.BggId).ToArray();
        return BuildConfirmation(draft, selectedBase, selectedExpansions);
    }

    public async Task CancelAsync(Guid publicId, long campId, long participantId,
        CancellationToken cancellationToken)
    {
        var import = await RequireOwnedAsync(publicId, campId, participantId, cancellationToken);
        if (import.Status is CampBggImportStatus.Completed or CampBggImportStatus.Confirmed
            or CampBggImportStatus.Failed or CampBggImportStatus.Cancelled) return;
        var now = timeProvider.GetUtcNow();
        import.CancellationRequestedAt = now;
        if (import.Status == CampBggImportStatus.Queued) import.Status = CampBggImportStatus.Cancelled;
        import.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RetryAsync(Guid publicId, long campId, long participantId,
        CancellationToken cancellationToken)
    {
        var import = await RequireOwnedAsync(publicId, campId, participantId, cancellationToken);
        if (import.Status != CampBggImportStatus.Failed) throw new InvalidOperationException("Повтор доступен только после ошибки.");
        await participationPolicy.RequireCompleteRegistrationAsync(campId, participantId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        import.Status = CampBggImportStatus.Queued;
        import.Error = null; import.LeaseId = null; import.LeaseExpiresAt = null;
        import.CancellationRequestedAt = null; import.UpdatedAt = now; import.ExpiresAt = now.AddDays(7);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CampBggImport> RequireOwnedAsync(Guid publicId, long campId, long participantId,
        CancellationToken cancellationToken)
    {
        var import = await dbContext.CampBggImports.SingleOrDefaultAsync(x => x.PublicId == publicId,
            cancellationToken) ?? throw new KeyNotFoundException("Импорт не найден.");
        EnsureOwner(import, campId, participantId);
        return import;
    }

    public static void EnsureOwner(CampBggImport import, long campId, long participantId)
    {
        if (import.CampId != campId || import.ParticipantId != participantId)
            throw new UnauthorizedAccessException("Импорт принадлежит другому участнику или кэмпу.");
    }
}
