using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

public sealed class CampBggImportWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<CampBggImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);
        do
        {
            try
            {
                while (await ProcessOneAsync(stoppingToken)) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Camp BGG import worker iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> ProcessOneAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();
        var leaseId = Guid.NewGuid();
        CampBggImport? import;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken))
        {
            import = await dbContext.CampBggImports
                .FromSqlInterpolated($$"""
                    SELECT * FROM "CampBggImports"
                    WHERE ("Status" = 0 OR ("Status" = 1 AND "LeaseExpiresAt" < {{now}}))
                      AND "CancellationRequestedAt" IS NULL AND "ExpiresAt" > {{now}}
                    ORDER BY "CreatedAt" FOR UPDATE SKIP LOCKED LIMIT 1
                    """)
                .SingleOrDefaultAsync(stoppingToken);
            if (import is null)
            {
                await transaction.CommitAsync(stoppingToken);
                return false;
            }
            import.Status = CampBggImportStatus.Running;
            import.LeaseId = leaseId;
            import.LeaseExpiresAt = now.AddMinutes(30);
            import.AttemptCount++;
            import.Error = null;
            import.ProgressCurrent = 0;
            import.ProgressTotal = 2;
            import.UpdatedAt = now;
            await dbContext.SaveChangesAsync(stoppingToken);
            await transaction.CommitAsync(stoppingToken);
        }

        try
        {
            var loader = scope.ServiceProvider.GetRequiredService<CampBggImportService>();
            var draft = await loader.LoadDraftAsync(import.BggUsername, stoppingToken,
                async (current, total) =>
                {
                    await dbContext.Entry(import).ReloadAsync(stoppingToken);
                    if (import.LeaseId != leaseId)
                        throw new InvalidOperationException("Задача импорта передана другому обработчику.");
                    if (import.CancellationRequestedAt is not null)
                        throw new OperationCanceledException("Импорт отменён пользователем.");
                    import.ProgressCurrent = current;
                    import.ProgressTotal = total;
                    import.LeaseExpiresAt = timeProvider.GetUtcNow().AddMinutes(30);
                    import.UpdatedAt = timeProvider.GetUtcNow();
                    await dbContext.SaveChangesAsync(stoppingToken);
                });
            await dbContext.Entry(import).ReloadAsync(stoppingToken);
            if (import.LeaseId != leaseId) return true;
            if (import.CancellationRequestedAt is not null)
            {
                import.Status = CampBggImportStatus.Cancelled;
            }
            else
            {
                import.DraftJson = CampBggImportDraftSerializer.Serialize(draft);
                import.ProgressCurrent = 2;
                import.ProgressTotal = 2;
                import.Status = CampBggImportStatus.Completed;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            await dbContext.Entry(import).ReloadAsync(CancellationToken.None);
            if (import.LeaseId != leaseId) return true;
            import.Status = import.CancellationRequestedAt is null
                ? CampBggImportStatus.Failed : CampBggImportStatus.Cancelled;
            import.Error = exception.Message.Length <= 2000 ? exception.Message : exception.Message[..2000];
            logger.LogWarning(exception, "Camp BGG import {ImportPublicId} failed.", import.PublicId);
        }

        import.LeaseId = null; import.LeaseExpiresAt = null; import.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}
