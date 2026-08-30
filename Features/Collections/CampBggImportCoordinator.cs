using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

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
    CampImportOverrideResolution? OverrideResolution);
public sealed record CampImportConfirmationResult(int Added, IReadOnlyDictionary<CampImportSkipReason, int> Skipped,
    bool HasOverridableItems, bool WasAlreadyConfirmed = false);

public sealed class CampBggImportCoordinator(
    AppDbContext dbContext,
    CampContributionSelectionService contributionService,
    TimeProvider timeProvider)
{
    public async Task<CampBggImport> QueueAsync(long campId, long participantId, string username,
        CancellationToken cancellationToken)
    {
        var camp = await dbContext.Camps.AsNoTracking().SingleOrDefaultAsync(x => x.Id == campId, cancellationToken)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        if (camp.Status != CampStatus.Active) throw new InvalidOperationException("Кэмп не принимает новые импорты.");
        if (!await dbContext.CampRegistrations.AnyAsync(x => x.CampId == campId && x.ParticipantId == participantId,
                cancellationToken))
            throw new UnauthorizedAccessException("Сначала завершите регистрацию в кэмпе.");

        var now = timeProvider.GetUtcNow();
        var import = new CampBggImport
        {
            PublicId = Guid.NewGuid(), CampId = campId, ParticipantId = participantId,
            BggUsername = username, Status = CampBggImportStatus.Queued,
            CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddDays(7)
        };
        dbContext.CampBggImports.Add(import);
        await dbContext.SaveChangesAsync(cancellationToken);
        return import;
    }

    public async Task<CampBggImportView> GetAsync(Guid publicId, long campId, long participantId,
        CancellationToken cancellationToken)
    {
        var import = await RequireOwnedAsync(publicId, campId, participantId, cancellationToken);
        return new CampBggImportView(import.PublicId, import.Status, import.BggUsername,
            import.ProgressCurrent, import.ProgressTotal,
            import.Status is CampBggImportStatus.Completed or CampBggImportStatus.Confirmed
                ? CampBggImportDraftSerializer.Deserialize(import.DraftJson) : null,
            import.Error, import.UpdatedAt, import.ExpiresAt, import.OverrideResolution);
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
            await transaction.CommitAsync(cancellationToken);
            return Result(confirmedDraft, selectedBaseGameIds, selectedExpansionIds) with { WasAlreadyConfirmed = true };
        }
        if (import.Status != CampBggImportStatus.Completed) throw new InvalidOperationException("Импорт ещё не готов.");
        var draft = CampBggImportDraftSerializer.Deserialize(import.DraftJson);
        await contributionService.ConfirmImportAsync(campId, participantId, draft,
            selectedBaseGameIds, selectedExpansionIds, timeProvider.GetUtcNow(), cancellationToken);
        import.Status = CampBggImportStatus.Confirmed;
        import.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result(draft, selectedBaseGameIds, selectedExpansionIds);
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
        var draft = CampBggImportDraftSerializer.Deserialize(import.DraftJson);
        if (!draft.Items.Any(x => x.SkipReason == CampImportSkipReason.AlreadyInBaseCollection && x.IsOverridable))
            throw new InvalidOperationException("В импорте нет копий, которые можно добавить.");
        if (resolution == CampImportOverrideResolution.AddPersonalCopies)
            await contributionService.AddSoftSkippedCopiesAsync(campId, participantId, draft,
                timeProvider.GetUtcNow(), cancellationToken);
        import.OverrideResolution = resolution;
        import.OverrideResolvedAt = timeProvider.GetUtcNow();
        import.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static CampImportConfirmationResult Result(CampBggImportDraft draft,
        IReadOnlyCollection<long> selectedBaseIds, IReadOnlyCollection<long> selectedExpansionIds)
    {
        var selectedBase = selectedBaseIds.ToHashSet(); var selectedExpansions = selectedExpansionIds.ToHashSet();
        var added = draft.Items.Count(x => x.SkipReason is null && (x.ItemType == CampContributionItemType.BaseGame
            ? selectedBase.Contains(x.BggId) : selectedExpansions.Contains(x.BggId)));
        var skipped = draft.Items.Where(x => x.SkipReason.HasValue).GroupBy(x => x.SkipReason!.Value)
            .ToDictionary(x => x.Key, x => x.Count());
        return new CampImportConfirmationResult(added, skipped,
            draft.Items.Any(x => x.SkipReason == CampImportSkipReason.AlreadyInBaseCollection && x.IsOverridable));
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

    private static void EnsureOwner(CampBggImport import, long campId, long participantId)
    {
        if (import.CampId != campId || import.ParticipantId != participantId)
            throw new UnauthorizedAccessException("Импорт принадлежит другому участнику или кэмпу.");
    }
}
