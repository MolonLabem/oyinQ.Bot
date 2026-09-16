using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

public sealed record ClubCollectionState(
    ClubCollectionDocument Collection,
    long Revision,
    DateTimeOffset UpdatedAt, bool CanEdit = true, long? SourceClubId = null);

public sealed class ClubCollectionConflictException(long currentRevision)
    : InvalidOperationException("Коллекция была изменена другим администратором.")
{
    public long CurrentRevision { get; } = currentRevision;
}

public sealed class ClubCollectionService(AppDbContext dbContext)
{
    public async Task<ClubCollectionState> GetAsync(long clubId, CancellationToken cancellationToken)
    {
        var club = await dbContext.Clubs.AsNoTracking().SingleAsync(x => x.Id == clubId, cancellationToken);
        var source = await new SharedCollectionReader(dbContext).SourceAsync(clubId, cancellationToken);
        return new(source.ReadCollection(), source.CollectionRevision, source.UpdatedAt, club.SourceClubId is null,
            club.SourceClubId is null ? null : source.Id);
    }

    public async Task AddOrReplaceGameAsync(
        long clubId,
        ClubCollectionGame game,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var club = await LockClubAsync(clubId, cancellationToken);
        EnsureRevision(club, expectedRevision);
        club.EnsureOwnCollection();
        var current = club.ReadCollection();
        club.ReplaceCollection(ClubCollectionEditor.AddOrReplace(current, game), now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> RemoveGameAsync(
        long clubId,
        long bggId,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var club = await LockClubAsync(clubId, cancellationToken);
        EnsureRevision(club, expectedRevision);
        club.EnsureOwnCollection();
        var current = club.ReadCollection();
        var updated = ClubCollectionEditor.Remove(current, bggId);
        if (updated.Games.Count == current.Games.Count)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        club.ReplaceCollection(updated, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task ReplaceAsync(
        long clubId,
        ClubCollectionDocument document,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ClubCollectionSerializer.Validate(document);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var club = await LockClubAsync(clubId, cancellationToken);
        EnsureRevision(club, expectedRevision);
        club.ReplaceCollection(document, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CopyFromClubAsync(
        long clubId,
        long sourceClubId,
        long expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (clubId == sourceClubId)
            throw new InvalidOperationException("Выберите другой клуб как источник коллекции.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Serialize link changes across instances, including reciprocal link attempts.
        await dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(719834215)", cancellationToken);
        var club = await LockClubAsync(clubId, cancellationToken);
        EnsureRevision(club, expectedRevision);
        _ = await new SharedCollectionReader(dbContext).SourceAsync(sourceClubId, cancellationToken, clubId);
        if (club.SourceClubId == sourceClubId) { await transaction.CommitAsync(cancellationToken); return; }
        club.SourceClubId = sourceClubId;
        club.CollectionRevision++;
        club.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Club> LockClubAsync(long clubId, CancellationToken cancellationToken) =>
        await dbContext.Clubs
            .FromSqlInterpolated($"SELECT * FROM \"Clubs\" WHERE \"Id\" = {clubId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("Клуб не найден.");

    private static void EnsureRevision(Club club, long expectedRevision)
    {
        if (club.CollectionRevision != expectedRevision)
            throw new ClubCollectionConflictException(club.CollectionRevision);
    }
}

public static class ClubCollectionEditor
{
    public static ClubCollectionDocument AddOrReplace(ClubCollectionDocument document, ClubCollectionGame game) =>
        new(
            ClubCollectionDocument.CurrentVersion,
            document.Games.Where(value => value.BggId != game.BggId)
                .Append(game)
                .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());

    public static ClubCollectionDocument Remove(ClubCollectionDocument document, long bggId) =>
        new(
            ClubCollectionDocument.CurrentVersion,
            document.Games.Where(value => value.BggId != bggId).ToArray());
}
