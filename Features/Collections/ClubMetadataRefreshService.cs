using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public sealed record ClubMetadataRefreshView(Guid PublicId, ClubMetadataRefreshStatus Status,
    int ProgressCurrent, int ProgressTotal, string? Error, DateTimeOffset UpdatedAt, int? UpdatedGames);

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
        club.EnsureOwnCollection();
        var existing = await dbContext.ClubMetadataRefreshes.SingleOrDefaultAsync(x => x.ClubId == clubId
            && (x.Status == ClubMetadataRefreshStatus.Queued || x.Status == ClubMetadataRefreshStatus.Running), cancellationToken);
        if (existing is not null) return ToView(existing);
        var now = timeProvider.GetUtcNow();
        var job = new ClubMetadataRefresh { PublicId = Guid.NewGuid(), ClubId = clubId,
            Status = ClubMetadataRefreshStatus.Queued, BggIdsJson = JsonSerializer.Serialize(ids, JsonOptions),
            ProgressTotal = ids.Length, StagedCollectionJson = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty),
            CreatedAt = now, UpdatedAt = now };
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
        // A worker may reuse its scope while other workers advance the persisted job.
        // Claim from current database values, never a previous iteration's tracked entity.
        dbContext.ChangeTracker.Clear();
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
            if (job.StagedCollectionJson is null)
            {
                // Older workers published each item. Re-fetch the entire job after upgrade;
                // never reset the live collection or its historical revision.
                job.ProgressCurrent = 0;
                job.ProgressTotal = ids.Length;
                job.StagedCollectionJson = ClubCollectionSerializer.Serialize(ClubCollectionDocument.Empty);
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
            ClubCollectionGame? refreshed = null;
            if (claimedIndex < ids.Length)
            {
                var details = await bggClient.GetGameDetailsAsync(ids[claimedIndex], cancellationToken)
                    ?? throw new InvalidOperationException("BGG не вернул данные игры. Повторите обновление позже.");
                if (details.Game.BggId != ids[claimedIndex])
                    throw new InvalidOperationException("BGG вернул данные другой игры.");
                refreshed = BggGameMapper.ToCollectionGame(details);
            }
            await using var finalize = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var job = await dbContext.ClubMetadataRefreshes
                .FromSqlInterpolated($"SELECT * FROM \"ClubMetadataRefreshes\" WHERE \"Id\" = {jobId} FOR UPDATE")
                .SingleAsync(cancellationToken);
            if (job.LeaseId != leaseId || job.Status != ClubMetadataRefreshStatus.Running)
            {
                await finalize.CommitAsync(cancellationToken);
                return true;
            }
            if (job.ProgressCurrent != claimedIndex)
                throw new InvalidOperationException("Прогресс обновления метаданных изменился вне активной аренды.");
            var staged = ClubCollectionSerializer.Deserialize(job.StagedCollectionJson);
            if (refreshed is not null)
            {
                staged = ClubCollectionEditor.AddOrReplace(staged, refreshed);
                job.StagedCollectionJson = ClubCollectionSerializer.Serialize(staged);
                job.ProgressCurrent++;
            }
            if (job.ProgressCurrent >= ids.Length)
            {
                if (!ids.ToHashSet().SetEquals(staged.Games.Select(game => game.BggId)))
                    throw new InvalidOperationException("Не все данные обновления сохранены. Запустите обновление заново.");
                var club = await dbContext.Clubs.FromSqlInterpolated($"SELECT * FROM \"Clubs\" WHERE \"Id\" = {clubId} FOR UPDATE")
                    .SingleAsync(cancellationToken);
                var current = club.ReadCollection();
                var metadata = staged.Games.ToDictionary(game => game.BggId);
                var changed = 0;
                var games = current.Games.Select(existing =>
                {
                    if (!metadata.TryGetValue(existing.BggId, out var value)) return existing;
                    var enriched = EnrichPreservingMembership(existing, value);
                    if (!ClubCollectionSerializer.ContentEquals(new(2, [existing]), new(2, [enriched]))) changed++;
                    return enriched;
                }).ToArray();
                var published = club.ReplaceCollection(new(ClubCollectionDocument.CurrentVersion, games), timeProvider.GetUtcNow());
                job.UpdatedGames = published ? changed : 0;
                job.Status = ClubMetadataRefreshStatus.Completed;
            }
            else job.Status = ClubMetadataRefreshStatus.Queued;
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
        job.ProgressCurrent, job.ProgressTotal,
        job.Status == ClubMetadataRefreshStatus.Failed ? "Не удалось обновить данные BGG. Коллекция не изменена; повторите позже." : null,
        job.UpdatedAt, job.UpdatedGames);

    public static ClubCollectionGame EnrichPreservingMembership(ClubCollectionGame existing, BggGameDetails details) =>
        EnrichPreservingMembership(existing, BggGameMapper.ToCollectionGame(details));

    public static ClubCollectionGame EnrichPreservingMembership(ClubCollectionGame existing, ClubCollectionGame metadata)
    {
        if (existing.BggId != metadata.BggId) throw new InvalidOperationException("Метаданные относятся к другой игре.");
        var expansions = existing.Expansions.Select(selected => metadata.Expansions
            .SingleOrDefault(value => value.BggId == selected.BggId)?.WithMetadataFallback(selected) ?? selected).ToArray();
        return metadata with { Expansions = expansions };
    }
}
