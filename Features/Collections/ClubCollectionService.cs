using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

public sealed class ClubCollectionService(AppDbContext dbContext)
{
    public async Task<ClubCollectionDocument> GetAsync(long clubId, CancellationToken cancellationToken)
    {
        var json = await dbContext.Clubs.AsNoTracking()
            .Where(value => value.Id == clubId)
            .Select(value => value.CollectionJson)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Club was not found.");
        return ClubCollectionSerializer.Deserialize(json);
    }

    public async Task AddOrReplaceGameAsync(
        long clubId,
        ClubCollectionGame game,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var club = await FindClubAsync(clubId, cancellationToken);
        var current = club.ReadCollection();
        club.ReplaceCollection(ClubCollectionEditor.AddOrReplace(current, game), now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveGameAsync(
        long clubId,
        long bggId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var club = await FindClubAsync(clubId, cancellationToken);
        var current = club.ReadCollection();
        var updated = ClubCollectionEditor.Remove(current, bggId);
        if (updated.Games.Count == current.Games.Count) return false;
        club.ReplaceCollection(updated, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReplaceAsync(
        long clubId,
        ClubCollectionDocument document,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ClubCollectionSerializer.Validate(document);
        var club = await FindClubAsync(clubId, cancellationToken);
        club.ReplaceCollection(document, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Club> FindClubAsync(long clubId, CancellationToken cancellationToken) =>
        await dbContext.Clubs.SingleOrDefaultAsync(value => value.Id == clubId, cancellationToken)
        ?? throw new KeyNotFoundException("Club was not found.");
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
