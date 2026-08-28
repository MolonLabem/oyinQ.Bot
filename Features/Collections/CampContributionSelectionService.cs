using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

public sealed record CampImportSelectionItem(
    long BggId,
    CampContributionItemType ItemType,
    long? ParentBggId,
    string Name,
    bool Selected,
    string? ThumbnailImageUrl = null,
    string? ImageUrl = null,
    int? MinPlayers = null,
    int? MaxPlayers = null,
    string? BestPlayers = null);

public sealed record CampImportSelectionGroup(
    CampImportSelectionItem BaseGame,
    IReadOnlyList<CampImportSelectionItem> Expansions,
    bool ShowMissingBaseWarning);

public sealed record EffectiveCampCatalogItem(
    long BggId,
    CampContributionItemType ItemType,
    long? ParentBggId,
    string Name,
    IReadOnlyList<long> ContributorParticipantIds);

public sealed class CampContributionSelectionService(AppDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<CampImportSelectionItem> SelectAll(
        IEnumerable<CampImportSelectionItem> items) =>
        items.Select(value => value with { Selected = true }).ToArray();

    public static bool NeedsMissingBaseWarning(
        CampImportSelectionItem expansion,
        IReadOnlyCollection<CampImportSelectionItem> items) =>
        expansion.ItemType == CampContributionItemType.Expansion
        && expansion.Selected
        && expansion.ParentBggId is { } parentId
        && !items.Any(value => value.ItemType == CampContributionItemType.BaseGame
            && value.BggId == parentId
            && value.Selected);

    public async Task SaveSelectionAsync(
        long campId,
        long participantId,
        IReadOnlyCollection<CampImportSelectionItem> selection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.CampRegistrations.AnyAsync(
                value => value.CampId == campId && value.ParticipantId == participantId,
                cancellationToken))
        {
            throw new UnauthorizedAccessException("Сначала завершите регистрацию в кэмпе.");
        }

        var duplicate = selection.GroupBy(value => new { value.BggId, value.ItemType }).FirstOrDefault(value => value.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException("Импорт содержит повторяющиеся элементы BGG.");
        }

        var selectedKeys = selection.Select(value => new { value.BggId, value.ItemType }).ToArray();
        var existing = (await dbContext.CampGameContributions
            .Where(value => value.CampId == campId && value.ParticipantId == participantId)
            .ToArrayAsync(cancellationToken))
            .Where(value => selectedKeys.Any(key => key.BggId == value.BggId && key.ItemType == value.ItemType))
            .ToArray();
        dbContext.CampGameContributions.RemoveRange(existing);
        dbContext.CampGameContributions.AddRange(selection.Where(value => value.Selected).Select(value =>
            new CampGameContribution
            {
                CampId = campId,
                ParticipantId = participantId,
                BggId = value.BggId,
                ItemType = value.ItemType,
                ParentBggId = value.ParentBggId,
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    value.Name,
                    value.ThumbnailImageUrl,
                    value.ImageUrl,
                    value.MinPlayers,
                    value.MaxPlayers,
                    value.BestPlayers
                }, JsonOptions),
                CreatedAt = now.ToUniversalTime(),
                UpdatedAt = now.ToUniversalTime()
            }));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EffectiveCampCatalogItem>> GetEffectiveContributionsAsync(
        long campId,
        CancellationToken cancellationToken)
    {
        var contributions = await dbContext.CampGameContributions.AsNoTracking()
            .Where(value => value.CampId == campId)
            .ToArrayAsync(cancellationToken);
        return MergeContributions(contributions);
    }

    public static IReadOnlyList<EffectiveCampCatalogItem> MergeContributions(
        IEnumerable<CampGameContribution> contributions) =>
        contributions
            .GroupBy(value => new { value.BggId, value.ItemType, value.ParentBggId })
            .Select(group => new EffectiveCampCatalogItem(
                group.Key.BggId,
                group.Key.ItemType,
                group.Key.ParentBggId,
                ReadName(group.First().SnapshotJson),
                group.Select(value => value.ParticipantId).Distinct().Order().ToArray()))
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ReadName(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("name").GetString() ?? "Без названия";
    }
}
