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
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
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
        dbContext.ClubMetadataRefreshes.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToView(job);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(job).State = EntityState.Detached;
            var concurrent = await dbContext.ClubMetadataRefreshes.AsNoTracking()
                .SingleAsync(x => x.ClubId == clubId && (x.Status == ClubMetadataRefreshStatus.Queued
                    || x.Status == ClubMetadataRefreshStatus.Running), cancellationToken);
            return ToView(concurrent);
        }
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
        var now = timeProvider.GetUtcNow();
        var leaseId = Guid.NewGuid();
        long jobId;
        long clubId;
        int claimedIndex;
        long[] ids;
        await using (var claim = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var job = await dbContext.ClubMetadataRefreshes.FromSqlInterpolated($$"""
                SELECT * FROM "ClubMetadataRefreshes"
                WHERE "Status" = 0 OR ("Status" = 1 AND "LeaseExpiresAt" < {{now}})
                ORDER BY "CreatedAt" FOR UPDATE SKIP LOCKED LIMIT 1
                """).SingleOrDefaultAsync(cancellationToken);
            if (job is null)
            {
                await claim.CommitAsync(cancellationToken);
                return false;
            }
            ids = JsonSerializer.Deserialize<long[]>(job.BggIdsJson, JsonOptions) ?? [];
            if (job.ProgressCurrent >= ids.Length)
            {
                job.Status = ClubMetadataRefreshStatus.Completed;
                job.LeaseId = null;
                job.LeaseExpiresAt = null;
                job.UpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);
                await claim.CommitAsync(cancellationToken);
                return true;
            }
            job.Status = ClubMetadataRefreshStatus.Running;
            job.LeaseId = leaseId;
            job.LeaseExpiresAt = now.Add(LeaseDuration);
            job.Error = null;
            job.UpdatedAt = now;
            jobId = job.Id;
            clubId = job.ClubId;
            claimedIndex = job.ProgressCurrent;
            await dbContext.SaveChangesAsync(cancellationToken);
            await claim.CommitAsync(cancellationToken);
        }
        dbContext.ChangeTracker.Clear();
        try
        {
            var details = await bggClient.GetGameDetailsAsync(ids[claimedIndex], cancellationToken);
            await using var finalize = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var job = await dbContext.ClubMetadataRefreshes
                .FromSqlInterpolated($"SELECT * FROM \"ClubMetadataRefreshes\" WHERE \"Id\" = {jobId} FOR UPDATE")
                .SingleAsync(cancellationToken);
            if (job.LeaseId != leaseId)
            {
                await finalize.CommitAsync(cancellationToken);
                return true;
            }
            if (job.ProgressCurrent != claimedIndex)
                throw new InvalidOperationException("Прогресс обновления метаданных изменился вне активной аренды.");
            if (details is not null)
            {
                var club = await dbContext.Clubs.FromSqlInterpolated($"SELECT * FROM \"Clubs\" WHERE \"Id\" = {clubId} FOR UPDATE")
                    .SingleAsync(cancellationToken);
                var document = club.ReadCollection();
                var existing = document.Games.SingleOrDefault(x => x.BggId == details.Game.BggId);
                if (existing is not null)
                {
                    var updated = EnrichPreservingMembership(existing, details);
                    club.ReplaceCollection(ClubCollectionEditor.AddOrReplace(document, updated), timeProvider.GetUtcNow());
                }
            }
            job.ProgressCurrent++;
            job.Status = job.ProgressCurrent >= job.ProgressTotal
                ? ClubMetadataRefreshStatus.Completed : ClubMetadataRefreshStatus.Queued;
            job.LeaseId = null;
            job.LeaseExpiresAt = null;
            job.UpdatedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            await finalize.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            dbContext.ChangeTracker.Clear();
            await using var failed = await dbContext.Database.BeginTransactionAsync(CancellationToken.None);
            var job = await dbContext.ClubMetadataRefreshes
                .FromSqlInterpolated($"SELECT * FROM \"ClubMetadataRefreshes\" WHERE \"Id\" = {jobId} FOR UPDATE")
                .SingleAsync(CancellationToken.None);
            if (job.LeaseId == leaseId)
            {
                job.Status = ClubMetadataRefreshStatus.Failed;
                job.Error = exception.Message[..Math.Min(2000, exception.Message.Length)];
                job.LeaseId = null;
                job.LeaseExpiresAt = null;
                job.UpdatedAt = timeProvider.GetUtcNow();
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            await failed.CommitAsync(CancellationToken.None);
        }
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
