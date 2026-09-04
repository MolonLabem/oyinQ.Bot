using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

public sealed class ParticipantCollectionService(AppDbContext dbContext)
{
    // All ownership writers serialize on the participant, independently of Camp registration.
    public async Task UpsertAsync(long participantId, IReadOnlyCollection<CampBggImportDraftItem> items,
        CollectionItemSource source, DateTimeOffset now, CancellationToken cancellationToken, bool preserveExisting = false)
    {
        var ownsTransaction = dbContext.Database.CurrentTransaction is null && dbContext.Database.IsRelational();
        await using var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        if (dbContext.Database.IsRelational())
            await dbContext.Participants.FromSqlInterpolated(
                $"SELECT * FROM \"Participants\" WHERE \"Id\" = {participantId} FOR UPDATE").SingleAsync(cancellationToken);
        var existing = await dbContext.ParticipantCollectionItems.Where(x => x.ParticipantId == participantId)
            .ToDictionaryAsync(x => (x.BggId, x.ItemType), cancellationToken);
        foreach (var item in items.DistinctBy(x => (x.BggId, x.ItemType)))
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(item.BggId);
            if (!Enum.IsDefined(item.ItemType)) throw new ArgumentException("Неизвестный тип игры.");
            if (preserveExisting && existing.ContainsKey((item.BggId, item.ItemType))) continue;
            if (!existing.TryGetValue((item.BggId, item.ItemType), out var row))
            {
                row = new ParticipantCollectionItem { ParticipantId = participantId, BggId = item.BggId,
                    ItemType = item.ItemType, Source = source, CreatedAt = now };
                dbContext.ParticipantCollectionItems.Add(row);
                existing.Add((item.BggId, item.ItemType), row);
            }
            // Import refreshes metadata but never takes ownership of a manual entry or removes anything.
            if (source == CollectionItemSource.Manual) row.Source = source;
            row.ParentBggId = item.ParentBggId;
            row.SnapshotJson = CollectionItemSnapshotSerializer.Serialize(item.Snapshot with
            { ParentBggIds = item.ParentBggIds ?? item.Snapshot.ParentBggIds
                ?? (item.ParentBggId is { } parent ? [parent] : []) });
            row.UpdatedAt = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveAsync(long participantId, long bggId, CollectionItemType itemType,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        if (dbContext.Database.IsRelational())
            await dbContext.Participants.FromSqlInterpolated(
                $"SELECT * FROM \"Participants\" WHERE \"Id\" = {participantId} FOR UPDATE").SingleAsync(cancellationToken);
        if (await dbContext.CampGameContributions.AnyAsync(x => x.ParticipantId == participantId
                && x.BggId == bggId && x.ItemType == itemType && x.Camp.Status == CampStatus.Active,
                cancellationToken))
            throw new InvalidOperationException("Сначала уберите игру из доступных в активных кэмпах.");
        var row = await dbContext.ParticipantCollectionItems.SingleOrDefaultAsync(x => x.ParticipantId == participantId
            && x.BggId == bggId && x.ItemType == itemType, cancellationToken);
        if (row is null) return;
        dbContext.Remove(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}
