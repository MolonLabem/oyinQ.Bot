using System.Net;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Games;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;
using Telegram.Bot;

namespace oyinQ.Bot.Features.Collections;

public sealed class CollectionImportWorker(
    IServiceScopeFactory scopeFactory,
    ITelegramBotClient botClient,
    IOptions<BggOptions> bggOptions,
    ILogger<CollectionImportWorker> logger)
    : BackgroundService
{
    private const int MaxImportsPerPass = 30;
    private const int BggStepSize = 140;
    private const string BggUnavailableReason = "BGG пока недоступен — ждём подтверждение API-доступа.";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SoftPassLimit = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StuckImportAge = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!bggOptions.Value.IsAvailable)
                {
                    await FailUnavailableBggImportsAsync(stoppingToken);
                }

                await RecoverStuckImportsAsync(stoppingToken);
                await ProcessPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Collection import worker pass failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessPassAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var processedImportIds = new HashSet<long>();
        for (var processed = 0;
             processed < MaxImportsPerPass && stopwatch.Elapsed < SoftPassLimit;
             processed++)
        {
            var importId = await TryClaimNextAsync(processedImportIds, cancellationToken);
            if (importId is null)
            {
                return;
            }

            processedImportIds.Add(importId.Value);
            await ProcessImportAsync(importId.Value, cancellationToken);
        }
    }

    private async Task<long?> TryClaimNextAsync(
        IReadOnlyCollection<long> excludedImportIds,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bggAvailable = bggOptions.Value.IsAvailable;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var importId = await dbContext.CollectionImports
                .Where(value => value.Status == ImportStatus.Pending
                    && value.Provider == ExternalGameProvider.Bgg
                    && bggAvailable
                    && !excludedImportIds.Contains(value.Id))
                .OrderBy(value => value.CreatedAt)
                .Select(value => (long?)value.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (importId is null)
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var claimed = await dbContext.CollectionImports
                .Where(value => value.Id == importId.Value && value.Status == ImportStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(value => value.Status, ImportStatus.Running)
                        .SetProperty(value => value.StartedAt, now)
                        .SetProperty(value => value.UpdatedAt, now)
                        .SetProperty(value => value.Error, (string?)null),
                    cancellationToken);
            if (claimed == 1)
            {
                return importId.Value;
            }
        }

        return null;
    }

    private async Task ProcessImportAsync(long importId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dedupService = scope.ServiceProvider.GetRequiredService<GameDedupService>();
        var import = await dbContext.CollectionImports.SingleAsync(
            value => value.Id == importId,
            cancellationToken);

        try
        {
            if (import.Provider != ExternalGameProvider.Bgg)
            {
                import.Status = ImportStatus.Failed;
                import.Error = "Этот источник импорта больше не поддерживается. Используйте BGG.";
                import.CompletedAt = DateTimeOffset.UtcNow;
                import.UpdatedAt = import.CompletedAt.Value;
                await dbContext.SaveChangesAsync(cancellationToken);
                await NotifyFailureSafeAsync(import, cancellationToken);
                return;
            }

            if (!bggOptions.Value.IsAvailable)
            {
                await FailImportAsUnavailableAsync(import, dbContext, cancellationToken);
                return;
            }

            var client = scope.ServiceProvider.GetRequiredService<IBoardGameGeekClient>();
            await ProcessBggAsync(import, client, dedupService, dbContext, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Collection import {ImportId} failed for provider {Provider}.",
                import.Id,
                import.Provider);

            import.Status = ImportStatus.Failed;
            import.Error = GetUserFacingError(import.Provider, exception);
            import.CompletedAt = DateTimeOffset.UtcNow;
            import.UpdatedAt = import.CompletedAt.Value;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await NotifyFailureSafeAsync(import, CancellationToken.None);
        }
    }

    private async Task ProcessBggAsync(
        CollectionImport import,
        IBoardGameGeekClient client,
        GameDedupService dedupService,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var progress = ParseProgress(import.ProgressJson);
        var step = await client.GetOwnedCollectionStepAsync(
            import.ExternalUsername,
            progress.Offset,
            BggStepSize,
            cancellationToken);

        var counts = await UpsertGamesAsync(import, step.Games, dedupService, cancellationToken);
        import.AddedCount += counts.Added;
        import.SkippedCount += counts.Skipped;

        var now = DateTimeOffset.UtcNow;
        import.ProgressJson = JsonSerializer.Serialize(new BggProgress(step.NextOffset, step.Total));
        import.UpdatedAt = now;

        if (step.NextOffset < step.Total)
        {
            import.Status = ImportStatus.Pending;
            import.StartedAt = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        import.Status = ImportStatus.Completed;
        import.CompletedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifySuccessSafeAsync(import, cancellationToken);
    }

    private static async Task<(int Added, int Skipped)> UpsertGamesAsync(
        CollectionImport import,
        IReadOnlyList<ExternalGame> games,
        GameDedupService dedupService,
        CancellationToken cancellationToken)
    {
        var source = import.Target == ImportTarget.Club
            ? GameCopySource.Club
            : GameCopySource.Personal;
        var bringStatus = import.Target == ImportTarget.Club
            ? BringStatus.Bringing
            : BringStatus.Maybe;
        long? ownerParticipantId = import.Target == ImportTarget.Club
            ? null
            : import.ParticipantId
                ?? throw new InvalidOperationException("Personal collection import has no participant.");
        var addedCount = 0;
        var skippedCount = 0;

        foreach (var externalGame in games)
        {
            var game = await dedupService.FindOrCreateAsync(externalGame, cancellationToken);
            var added = await dedupService.AddImportedCopyIfMissingAsync(
                game.Id,
                ownerParticipantId,
                source,
                bringStatus,
                cancellationToken);

            if (added)
            {
                addedCount++;
            }
            else
            {
                skippedCount++;
            }
        }

        return (addedCount, skippedCount);
    }

    private async Task FailUnavailableBggImportsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var imports = await dbContext.CollectionImports
            .Where(value => value.Provider == ExternalGameProvider.Bgg
                && (value.Status == ImportStatus.Pending || value.Status == ImportStatus.Running))
            .ToListAsync(cancellationToken);
        if (imports.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var import in imports)
        {
            import.Status = ImportStatus.Failed;
            import.Error = BggUnavailableReason;
            import.CompletedAt = now;
            import.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Failed {Count} queued BGG imports because BGG integration is disabled.",
            imports.Count);

        foreach (var import in imports)
        {
            await NotifyFailureSafeAsync(import, cancellationToken);
        }
    }

    private async Task FailImportAsUnavailableAsync(
        CollectionImport import,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        import.Status = ImportStatus.Failed;
        import.Error = BggUnavailableReason;
        import.CompletedAt = now;
        import.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyFailureSafeAsync(import, cancellationToken);
    }

    private async Task RecoverStuckImportsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTimeOffset.UtcNow - StuckImportAge;
        var now = DateTimeOffset.UtcNow;

        var recovered = await dbContext.CollectionImports
            .Where(value => value.Status == ImportStatus.Running && value.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.Status, ImportStatus.Pending)
                    .SetProperty(value => value.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(value => value.UpdatedAt, now),
                cancellationToken);

        if (recovered > 0)
        {
            logger.LogWarning("Recovered {Count} stuck collection imports.", recovered);
        }
    }

    private async Task NotifySuccessSafeAsync(CollectionImport import, CancellationToken cancellationToken)
    {
        try
        {
            var target = import.Target == ImportTarget.Club ? " коллекции клуба" : string.Empty;
            await botClient.SendMessage(
                import.RequestedByTelegramUserId,
                $"✅ Импорт{target} завершён\n\nДобавлено: {import.AddedCount}\nУже было: {import.SkippedCount}",
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not notify about completed import {ImportId}.", import.Id);
        }
    }

    private async Task NotifyFailureSafeAsync(CollectionImport import, CancellationToken cancellationToken)
    {
        try
        {
            await botClient.SendMessage(
                import.RequestedByTelegramUserId,
                $"❌ Импорт не завершён\n\n{import.Error}",
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not notify about failed import {ImportId}.", import.Id);
        }
    }

    private static string GetUserFacingError(
        ExternalGameProvider provider,
        Exception exception) =>
        exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                when provider == ExternalGameProvider.Bgg
                => "BGG отклонил запрос. Проверьте BGG_API_TOKEN и регистрацию приложения.",
            HttpRequestException when provider == ExternalGameProvider.Bgg
                => "BGG временно не отвечает. Попробуйте импорт ещё раз позже.",
            _ => "Не удалось импортировать коллекцию. Попробуйте позже."
        };

    private static BggProgress ParseProgress(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BggProgress(0, 0);
        }

        try
        {
            return JsonSerializer.Deserialize<BggProgress>(json) ?? new BggProgress(0, 0);
        }
        catch (JsonException)
        {
            return new BggProgress(0, 0);
        }
    }

    private sealed record BggProgress(
        [property: JsonPropertyName("offset")] int Offset,
        [property: JsonPropertyName("total")] int Total);
}
