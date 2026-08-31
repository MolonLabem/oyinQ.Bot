using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.Collections;

public sealed record CampImportSelectionItem(long BggId, CampContributionItemType ItemType, long? ParentBggId,
    string Name, bool Selected, string? ThumbnailImageUrl = null, string? ImageUrl = null,
    int? MinPlayers = null, int? MaxPlayers = null, string? BestPlayers = null,
    IReadOnlyList<string>? Types = null, IReadOnlyList<string>? Categories = null,
    string? Description = null, int? YearPublished = null, int? MinPlayTimeMinutes = null,
    int? MaxPlayTimeMinutes = null, int? MinAge = null, GameType Type = GameType.Other,
    IReadOnlyList<GameTaxonomyItem>? Subdomains = null,
    IReadOnlyList<GameTaxonomyItem>? CategoryItems = null,
    IReadOnlyList<GameTaxonomyItem>? Mechanics = null,
    IReadOnlyList<long>? ParentBggIds = null);

public sealed record CampImportSelectionGroup(CampImportSelectionItem BaseGame,
    IReadOnlyList<CampImportSelectionItem> Expansions, bool ShowMissingBaseWarning);

public sealed record EffectiveCampCatalogItem(long BggId, CampContributionItemType ItemType,
    IReadOnlyList<long> ParentBggIds, CampContributionSnapshot Snapshot, int CopyCount,
    IReadOnlyList<CampCatalogProvider> Providers)
{
    public string Name => Snapshot.Name;
    public long? ParentBggId => ParentBggIds.Count == 0 ? null : ParentBggIds[0];
    public IReadOnlyList<long> ContributorParticipantIds => Providers
        .Where(x => x.ParticipantId.HasValue).Select(x => x.ParticipantId!.Value).Distinct().Order().ToArray();
}

public sealed record CampCatalogProvider(long? ParticipantId, string DisplayName, string? City,
    CampContributionSource? Source, CampBringCommitment Commitment = CampBringCommitment.Available,
    bool IsCurrentUser = false, string? ContactUrl = null);

public sealed class CampContributionSelectionService(AppDbContext dbContext, CampParticipationPolicy participationPolicy)
{
    public static IReadOnlyList<EffectiveCampCatalogItem> MergeContributions(
        IEnumerable<CampGameContribution> contributions) => contributions
        .GroupBy(x => new { x.BggId, x.ItemType })
        .Select(group => new EffectiveCampCatalogItem(group.Key.BggId, group.Key.ItemType,
            ParentIds(group), RichestSnapshot(group.Select(value => value.ReadSnapshot())),
            group.Select(x => x.ParticipantId).Distinct().Count(),
            group.Select(x => new CampCatalogProvider(x.ParticipantId,
                $"Participant {x.ParticipantId}", null, x.Source, x.Commitment)).ToArray()))
        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<CampImportSelectionItem> SelectAll(IEnumerable<CampImportSelectionItem> items) =>
        items.Select(value => value with { Selected = true }).ToArray();

    public static bool NeedsMissingBaseWarning(CampImportSelectionItem expansion,
        IReadOnlyCollection<CampImportSelectionItem> items)
    {
        if (expansion.ItemType != CampContributionItemType.Expansion || !expansion.Selected)
            return false;
        var parents = expansion.ParentBggIds is { Count: > 0 }
            ? expansion.ParentBggIds : expansion.ParentBggId is { } parentId ? [parentId] : [];
        return parents.Count > 0 && !items.Any(value => value.ItemType == CampContributionItemType.BaseGame
            && parents.Contains(value.BggId) && value.Selected);
    }

    public async Task ConfirmImportAsync(long campId, long participantId, CampBggImportDraft draft,
        IReadOnlyCollection<long> selectedBaseGameIds, IReadOnlyCollection<long> selectedExpansionIds,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await participationPolicy.RequireCompleteRegistrationAsync(campId, participantId, cancellationToken);
        var selectedBase = selectedBaseGameIds.Distinct().ToHashSet();
        var selectedExpansions = selectedExpansionIds.Distinct().ToHashSet();
        var draftBase = draft.Items.Where(x => x.ItemType == CampContributionItemType.BaseGame).Select(x => x.BggId).ToHashSet();
        var draftExpansions = draft.Items.Where(x => x.ItemType == CampContributionItemType.Expansion).Select(x => x.BggId).ToHashSet();
        if (!selectedBase.IsSubsetOf(draftBase) || !selectedExpansions.IsSubsetOf(draftExpansions))
            throw new InvalidOperationException("Выбранный BGG ID отсутствует в серверном черновике импорта.");

        var existing = await dbContext.CampGameContributions
            .Where(x => x.CampId == campId && x.ParticipantId == participantId)
            .ToArrayAsync(cancellationToken);
        var desired = draft.Items.Where(x => x.SkipReason is null && (x.ItemType == CampContributionItemType.BaseGame
                ? selectedBase.Contains(x.BggId) : selectedExpansions.Contains(x.BggId))
            )
            .ToDictionary(x => (x.BggId, x.ItemType));
        dbContext.CampGameContributions.RemoveRange(existing.Where(x =>
            x.Source == CampContributionSource.BggImport && !desired.ContainsKey((x.BggId, x.ItemType))));

        foreach (var item in desired.Values)
        {
            var contribution = existing.SingleOrDefault(x => x.BggId == item.BggId && x.ItemType == item.ItemType);
            if (contribution is not null && contribution.Source is CampContributionSource.Manual or CampContributionSource.Legacy)
                continue;
            if (contribution is null)
            {
                contribution = new CampGameContribution
                {
                    CampId = campId, ParticipantId = participantId, BggId = item.BggId,
                    ItemType = item.ItemType, Source = CampContributionSource.BggImport,
                    CreatedAt = now.ToUniversalTime()
                };
                dbContext.CampGameContributions.Add(contribution);
            }
            contribution.ParentBggId = item.ParentBggId;
            contribution.SnapshotJson = CampContributionSnapshotSerializer.Serialize(item.Snapshot with
            {
                ParentBggIds = item.ParentBggIds ?? item.Snapshot.ParentBggIds
                    ?? (item.ParentBggId is { } parent ? [parent] : [])
            });
            contribution.UpdatedAt = now.ToUniversalTime();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddSoftSkippedCopiesAsync(long campId, long participantId, CampBggImportDraft draft,
        IReadOnlyCollection<CampImportItemKey> selectedItems,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await participationPolicy.RequireCompleteRegistrationAsync(campId, participantId, cancellationToken);
        var selected = selectedItems.Select(x => (x.BggId, x.ItemType)).ToHashSet();
        var items = draft.Items.Where(x => x.SkipReason == CampImportSkipReason.AlreadyInBaseCollection
            && x.IsOverridable && selected.Contains((x.BggId, x.ItemType)));
        foreach (var item in items)
        {
            var existing = await dbContext.CampGameContributions.SingleOrDefaultAsync(x => x.CampId == campId
                && x.ParticipantId == participantId && x.BggId == item.BggId && x.ItemType == item.ItemType,
                cancellationToken);
            if (existing is not null) continue;
            dbContext.CampGameContributions.Add(new CampGameContribution
            {
                CampId = campId, ParticipantId = participantId, BggId = item.BggId, ItemType = item.ItemType,
                ParentBggId = item.ParentBggId, Source = CampContributionSource.BggImport,
                Commitment = CampBringCommitment.Available,
                SnapshotJson = CampContributionSnapshotSerializer.Serialize(item.Snapshot with
                {
                    ParentBggIds = item.ParentBggIds ?? item.Snapshot.ParentBggIds
                        ?? (item.ParentBggId is { } parent ? [parent] : [])
                }),
                CreatedAt = now, UpdatedAt = now
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddManualAsync(long campId, long participantId, long bggId,
        CampContributionItemType itemType, long? parentBggId, CampContributionSnapshot snapshot,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await participationPolicy.RequireCompleteRegistrationAsync(campId, participantId, cancellationToken);
        if (itemType == CampContributionItemType.BaseGame)
        {
            var baseCollectionJson = await dbContext.Camps.AsNoTracking()
                .Where(x => x.Id == campId).Select(x => x.BaseCollectionJson).SingleAsync(cancellationToken);
            if (ClubCollectionSerializer.Deserialize(baseCollectionJson).Games.Any(x => x.BggId == bggId))
                throw new InvalidOperationException("Эта игра уже есть в общей коллекции кэмпа. Добавить её как личную можно при импорте коллекции BGG.");
        }
        var contribution = await dbContext.CampGameContributions.SingleOrDefaultAsync(
            x => x.CampId == campId && x.ParticipantId == participantId && x.BggId == bggId && x.ItemType == itemType,
            cancellationToken);
        if (contribution is null)
        {
            contribution = new CampGameContribution
            {
                CampId = campId, ParticipantId = participantId, BggId = bggId,
                ItemType = itemType, CreatedAt = now.ToUniversalTime()
            };
            dbContext.CampGameContributions.Add(contribution);
        }
        contribution.Source = CampContributionSource.Manual;
        contribution.ParentBggId = parentBggId;
        contribution.SnapshotJson = CampContributionSnapshotSerializer.Serialize(snapshot with
        {
            ParentBggIds = snapshot.ParentBggIds ?? (parentBggId is { } parent ? [parent] : [])
        });
        contribution.UpdatedAt = now.ToUniversalTime();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(long campId, long participantId, long bggId,
        CampContributionItemType itemType, CancellationToken cancellationToken)
    {
        await participationPolicy.RequireCompleteRegistrationAsync(campId, participantId, cancellationToken);
        var contribution = await dbContext.CampGameContributions.SingleOrDefaultAsync(
            x => x.CampId == campId && x.ParticipantId == participantId && x.BggId == bggId && x.ItemType == itemType,
            cancellationToken);
        if (contribution is null) return;
        dbContext.Remove(contribution);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetCommitmentAsync(long campId, long participantId, long bggId,
        CampContributionItemType itemType, CampBringCommitment commitment, CancellationToken cancellationToken)
    {
        await participationPolicy.RequireCompleteRegistrationAsync(campId, participantId, cancellationToken);
        var contribution = await dbContext.CampGameContributions.SingleOrDefaultAsync(
            x => x.CampId == campId && x.ParticipantId == participantId && x.BggId == bggId && x.ItemType == itemType,
            cancellationToken) ?? throw new KeyNotFoundException("Игра отсутствует в вашем списке.");
        contribution.Commitment = commitment;
        contribution.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EffectiveCampCatalogItem>> GetEffectiveContributionsAsync(
        long campId, CancellationToken cancellationToken, long? currentParticipantId = null)
    {
        var values = await dbContext.CampGameContributions.AsNoTracking()
            .Where(x => x.CampId == campId)
            .Select(x => new { Contribution = x, x.Participant.DisplayName, x.Participant.PreferredDisplayName,
                x.Participant.TelegramUserId, x.Participant.TelegramUsername,
                CampDisplayName = x.Participant.CampRegistrations.Where(r => r.CampId == campId)
                    .Select(r => r.DisplayName).SingleOrDefault(),
                City = x.Participant.CampRegistrations.Where(r => r.CampId == campId).Select(r => r.City).SingleOrDefault() })
            .ToArrayAsync(cancellationToken);
        return values.GroupBy(x => new { x.Contribution.BggId, x.Contribution.ItemType })
            .Select(group => new EffectiveCampCatalogItem(group.Key.BggId, group.Key.ItemType,
                ParentIds(group.Select(x => x.Contribution)),
                RichestSnapshot(group.Select(value => value.Contribution.ReadSnapshot())),
                group.Select(x => x.Contribution.ParticipantId).Distinct().Count(),
                group.Select(x => new CampCatalogProvider(x.Contribution.ParticipantId,
                    x.CampDisplayName ?? x.PreferredDisplayName ?? x.DisplayName, x.City, x.Contribution.Source,
                    x.Contribution.Commitment, x.Contribution.ParticipantId == currentParticipantId,
                    ParticipantPresentation.GetContactUrl(x.TelegramUserId, x.TelegramUsername))).ToArray()))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static CampContributionSnapshot RichestSnapshot(IEnumerable<CampContributionSnapshot> snapshots) =>
        snapshots.OrderByDescending(snapshot =>
            (snapshot.ThumbnailImageUrl is null ? 0 : 1)
            + (snapshot.ImageUrl is null ? 0 : 1)
            + (snapshot.MinPlayers.HasValue ? 1 : 0)
            + (snapshot.MaxPlayers.HasValue ? 1 : 0)
            + (snapshot.Types?.Count ?? 0)
            + (snapshot.Categories?.Count ?? 0)).First();

    private static IReadOnlyList<long> ParentIds(IEnumerable<CampGameContribution> values) => values
        .SelectMany(value => (value.ReadSnapshot().ParentBggIds ?? [])
            .Concat(value.ParentBggId is { } parent ? [parent] : []))
        .Where(x => x > 0).Distinct().Order().ToArray();

}
