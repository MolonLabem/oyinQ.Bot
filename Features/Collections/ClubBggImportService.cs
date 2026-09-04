using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public sealed record ClubBggImportView(Guid PublicId, string BggUsername, ClubBggImportStatus Status,
    int ProgressCurrent, int ProgressTotal, int AddedGames, int AddedExpansions, int OrphanExpansions,
    string? Error, DateTimeOffset UpdatedAt);

public sealed record ClubBggImportMergeResult(ClubCollectionDocument Document, int AddedGames,
    int AddedExpansions, int OrphanExpansions);

public sealed class ClubBggImportService(AppDbContext dbContext, CampBggImportService loader,
    TimeProvider timeProvider, ILogger<ClubBggImportService> logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);

    public async Task<ClubBggImportView> QueueAsync(long clubId, string input,
        CancellationToken cancellationToken)
    {
        var username = BggUsernameParser.Parse(input)
            ?? throw new InvalidOperationException("Введите имя пользователя BGG или ссылку на его профиль.");
        if (!await dbContext.Clubs.AsNoTracking().AnyAsync(x => x.Id == clubId, cancellationToken))
            throw new KeyNotFoundException("Клуб не найден.");
        var existing = await dbContext.ClubBggImports.SingleOrDefaultAsync(x => x.ClubId == clubId
            && (x.Status == ClubBggImportStatus.Queued || x.Status == ClubBggImportStatus.Running), cancellationToken);
        if (existing is not null) return ToView(existing);
        var now = timeProvider.GetUtcNow();
        var job = new ClubBggImport { PublicId = Guid.NewGuid(), ClubId = clubId, BggUsername = username,
            Status = ClubBggImportStatus.Queued, ProgressTotal = 2, CreatedAt = now, UpdatedAt = now };
        dbContext.ClubBggImports.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToView(job);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(job).State = EntityState.Detached;
            return ToView(await dbContext.ClubBggImports.AsNoTracking().SingleAsync(x => x.ClubId == clubId
                && (x.Status == ClubBggImportStatus.Queued || x.Status == ClubBggImportStatus.Running), cancellationToken));
        }
    }

    public async Task<ClubBggImportView> GetAsync(Guid publicId, long clubId,
        CancellationToken cancellationToken) => ToView(await dbContext.ClubBggImports.AsNoTracking()
        .SingleOrDefaultAsync(x => x.PublicId == publicId && x.ClubId == clubId, cancellationToken)
        ?? throw new KeyNotFoundException("Импорт не найден."));

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseId = Guid.NewGuid();
        ClubBggImport? job;
        await using (var claim = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            job = await dbContext.ClubBggImports.FromSqlInterpolated($$"""
                SELECT * FROM "ClubBggImports"
                WHERE "Status" = 0 OR ("Status" = 1 AND "LeaseExpiresAt" < {{now}})
                ORDER BY "CreatedAt" FOR UPDATE SKIP LOCKED LIMIT 1
                """).SingleOrDefaultAsync(cancellationToken);
            if (job is null) { await claim.CommitAsync(cancellationToken); return false; }
            job.Status = ClubBggImportStatus.Running;
            job.LeaseId = leaseId;
            job.LeaseExpiresAt = now.Add(LeaseDuration);
            job.ProgressCurrent = 0;
            job.ProgressTotal = 2;
            job.Error = null;
            job.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            await claim.CommitAsync(cancellationToken);
        }

        try
        {
            var selection = await loader.LoadSelectionAsync(job.BggUsername, cancellationToken,
                async (current, total) =>
                {
                    await dbContext.Entry(job).ReloadAsync(cancellationToken);
                    if (job.LeaseId != leaseId) throw new InvalidOperationException("Задача импорта передана другому обработчику.");
                    job.ProgressCurrent = current;
                    job.ProgressTotal = total;
                    job.LeaseExpiresAt = timeProvider.GetUtcNow().Add(LeaseDuration);
                    job.UpdatedAt = timeProvider.GetUtcNow();
                    await dbContext.SaveChangesAsync(cancellationToken);
                });

            await using var finalize = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            job = await dbContext.ClubBggImports
                .FromSqlInterpolated($"SELECT * FROM \"ClubBggImports\" WHERE \"Id\" = {job.Id} FOR UPDATE")
                .SingleAsync(cancellationToken);
            if (job.LeaseId != leaseId) { await finalize.CommitAsync(cancellationToken); return true; }
            var club = await dbContext.Clubs
                .FromSqlInterpolated($"SELECT * FROM \"Clubs\" WHERE \"Id\" = {job.ClubId} FOR UPDATE")
                .SingleAsync(cancellationToken);
            var merged = Merge(club.ReadCollection(), selection);
            club.ReplaceCollection(merged.Document, timeProvider.GetUtcNow());
            job.AddedGames = merged.AddedGames;
            job.AddedExpansions = merged.AddedExpansions;
            job.OrphanExpansions = merged.OrphanExpansions;
            job.ProgressCurrent = 2;
            job.ProgressTotal = 2;
            job.Status = ClubBggImportStatus.Completed;
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
            job = await dbContext.ClubBggImports
                .FromSqlInterpolated($"SELECT * FROM \"ClubBggImports\" WHERE \"Id\" = {job.Id} FOR UPDATE")
                .SingleAsync(CancellationToken.None);
            if (job.LeaseId == leaseId)
            {
                job.Status = ClubBggImportStatus.Failed;
                job.Error = exception.Message[..Math.Min(2000, exception.Message.Length)];
                job.LeaseId = null;
                job.LeaseExpiresAt = null;
                job.UpdatedAt = timeProvider.GetUtcNow();
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            await failed.CommitAsync(CancellationToken.None);
            logger.LogWarning(exception, "Club BGG import {ImportPublicId} failed.", job.PublicId);
        }
        return true;
    }

    public static ClubBggImportMergeResult Merge(ClubCollectionDocument current,
        IReadOnlyCollection<CampImportSelectionItem> imported)
    {
        var bases = imported.Where(x => x.ItemType == CollectionItemType.BaseGame)
            .DistinctBy(x => x.BggId).ToDictionary(x => x.BggId);
        var expansions = imported.Where(x => x.ItemType == CollectionItemType.Expansion)
            .DistinctBy(x => x.BggId).ToArray();
        var allBaseIds = current.Games.Select(x => x.BggId).Concat(bases.Keys).ToHashSet();
        var addedExpansions = 0;
        var result = current.Games.Select(existing => bases.TryGetValue(existing.BggId, out var item)
                ? ToGame(item, MergeExpansions(existing.Expansions, LinkedExpansions(existing.BggId, expansions), ref addedExpansions))
                : existing with { Expansions = MergeExpansions(existing.Expansions,
                    LinkedExpansions(existing.BggId, expansions), ref addedExpansions) })
            .ToList();
        foreach (var item in bases.Values.Where(x => current.Games.All(game => game.BggId != x.BggId)))
        {
            var selected = LinkedExpansions(item.BggId, expansions).ToArray();
            addedExpansions += selected.Length;
            result.Add(ToGame(item, selected));
        }
        var orphanCount = expansions.Count(expansion => (expansion.ParentBggIds ?? []).All(parent => !allBaseIds.Contains(parent)));
        return new ClubBggImportMergeResult(new ClubCollectionDocument(ClubCollectionDocument.CurrentVersion,
            result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray()),
            bases.Keys.Count(id => current.Games.All(game => game.BggId != id)), addedExpansions, orphanCount);
    }

    private static IEnumerable<ClubCollectionExpansion> LinkedExpansions(long baseId,
        IEnumerable<CampImportSelectionItem> expansions) => expansions
        .Where(x => (x.ParentBggIds ?? []).Contains(baseId))
        .Select(x => new ClubCollectionExpansion(x.BggId, x.Name, x.OriginalName));

    private static IReadOnlyList<ClubCollectionExpansion> MergeExpansions(
        IReadOnlyList<ClubCollectionExpansion> existing, IEnumerable<ClubCollectionExpansion> imported,
        ref int added)
    {
        var additions = imported.Where(x => existing.All(current => current.BggId != x.BggId)).ToArray();
        added += additions.Length;
        return existing.Concat(additions).DistinctBy(x => x.BggId)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ClubCollectionGame ToGame(CampImportSelectionItem item,
        IReadOnlyList<ClubCollectionExpansion> expansions)
    {
        var minPlayTime = item.MinPlayTimeMinutes is >= 0 ? item.MinPlayTimeMinutes : null;
        var maxPlayTime = item.MaxPlayTimeMinutes is >= 0 ? item.MaxPlayTimeMinutes : null;
        if (minPlayTime.HasValue && maxPlayTime.HasValue && minPlayTime > maxPlayTime)
            (minPlayTime, maxPlayTime) = (maxPlayTime, minPlayTime);
        return new ClubCollectionGame(item.BggId, item.Name, item.ThumbnailImageUrl, item.ImageUrl,
            item.MinPlayers, item.MaxPlayers, item.BestPlayers, expansions, item.Types, item.Categories,
            item.Description is { Length: > 20_000 } ? item.Description[..20_000] : item.Description,
            item.YearPublished is >= 1000 and <= 3000 ? item.YearPublished : null,
            minPlayTime, maxPlayTime, item.MinAge is >= 0 and <= 100 ? item.MinAge : null,
            item.Type, item.Subdomains, item.CategoryItems, item.Mechanics, item.OriginalName);
    }

    private static ClubBggImportView ToView(ClubBggImport job) => new(job.PublicId, job.BggUsername,
        job.Status, job.ProgressCurrent, job.ProgressTotal, job.AddedGames, job.AddedExpansions,
        job.OrphanExpansions, job.Error, job.UpdatedAt);
}
