using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public sealed record ClubMetadataRefreshView(Guid PublicId, ClubMetadataRefreshStatus Status,
    int ProgressCurrent, int ProgressTotal, string? Error, DateTimeOffset UpdatedAt);

public sealed class ClubMetadataRefreshService(AppDbContext dbContext, IBoardGameGeekClient bggClient,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClubMetadataRefreshView> QueueAsync(long clubId, CancellationToken cancellationToken)
    {
        var club = await dbContext.Clubs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clubId, cancellationToken)
            ?? throw new KeyNotFoundException("Клуб не найден.");
        var ids = club.ReadCollection().Games.Select(x => x.BggId).ToArray();
        var existing = await dbContext.ClubMetadataRefreshes.SingleOrDefaultAsync(x => x.ClubId == clubId
            && (x.Status == ClubMetadataRefreshStatus.Queued || x.Status == ClubMetadataRefreshStatus.Running), cancellationToken);
        if (existing is not null) return ToView(existing);
        var now = timeProvider.GetUtcNow();
        var job = new ClubMetadataRefresh { PublicId = Guid.NewGuid(), ClubId = clubId,
            Status = ClubMetadataRefreshStatus.Queued, BggIdsJson = JsonSerializer.Serialize(ids, JsonOptions),
            ProgressTotal = ids.Length, CreatedAt = now, UpdatedAt = now };
        dbContext.ClubMetadataRefreshes.Add(job); await dbContext.SaveChangesAsync(cancellationToken);
        return ToView(job);
    }

    public async Task<ClubMetadataRefreshView> GetAsync(Guid publicId, long clubId, CancellationToken cancellationToken)
    {
        var job = await dbContext.ClubMetadataRefreshes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PublicId == publicId && x.ClubId == clubId, cancellationToken)
            ?? throw new KeyNotFoundException("Обновление не найдено.");
        return ToView(job);
    }

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        var job = await dbContext.ClubMetadataRefreshes
            .Where(x => x.Status == ClubMetadataRefreshStatus.Queued || x.Status == ClubMetadataRefreshStatus.Running)
            .OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (job is null) return false;
        var ids = JsonSerializer.Deserialize<long[]>(job.BggIdsJson, JsonOptions) ?? [];
        if (job.ProgressCurrent >= ids.Length) { job.Status = ClubMetadataRefreshStatus.Completed; job.UpdatedAt = timeProvider.GetUtcNow(); await dbContext.SaveChangesAsync(cancellationToken); return true; }
        job.Status = ClubMetadataRefreshStatus.Running; job.Error = null; job.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        try
        {
            var details = await bggClient.GetGameDetailsAsync(ids[job.ProgressCurrent], cancellationToken);
            if (details is not null)
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                var club = await dbContext.Clubs.FromSqlInterpolated($"SELECT * FROM \"Clubs\" WHERE \"Id\" = {job.ClubId} FOR UPDATE")
                    .SingleAsync(cancellationToken);
                var document = club.ReadCollection();
                var existing = document.Games.SingleOrDefault(x => x.BggId == details.Game.BggId);
                if (existing is not null)
                {
                    var updated = EnrichPreservingMembership(existing, details);
                    club.ReplaceCollection(ClubCollectionEditor.AddOrReplace(document, updated), timeProvider.GetUtcNow());
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
            job.ProgressCurrent++; job.UpdatedAt = timeProvider.GetUtcNow();
            if (job.ProgressCurrent >= job.ProgressTotal) job.Status = ClubMetadataRefreshStatus.Completed;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            job.Status = ClubMetadataRefreshStatus.Failed;
            job.Error = exception.Message[..Math.Min(2000, exception.Message.Length)];
        }
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    private static ClubMetadataRefreshView ToView(ClubMetadataRefresh job) => new(job.PublicId, job.Status,
        job.ProgressCurrent, job.ProgressTotal, job.Error, job.UpdatedAt);

    public static ClubCollectionGame EnrichPreservingMembership(ClubCollectionGame existing, BggGameDetails details)
    {
        var game = details.Game;
        return existing with { Name = game.Name, ThumbnailImageUrl = game.ThumbnailImageUrl,
            ImageUrl = game.ImageUrl, MinPlayers = game.MinPlayers, MaxPlayers = game.MaxPlayers,
            BestPlayers = game.BestPlayers, Types = game.Types, Categories = game.Categories,
            Description = game.Description, YearPublished = game.YearPublished,
            MinPlayTimeMinutes = game.MinPlayTimeMinutes, MaxPlayTimeMinutes = game.MaxPlayTimeMinutes,
            MinAge = game.MinAge, Type = game.Type, Subdomains = game.Subdomains,
            CategoryItems = game.CategoryItems, Mechanics = game.Mechanics,
            Expansions = existing.Expansions.Select(selected => details.Expansions
                .Where(x => x.BggId == selected.BggId).Select(x => new ClubCollectionExpansion(x.BggId, x.Name))
                .SingleOrDefault() ?? selected).ToArray() };
    }
}
