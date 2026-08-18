using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Collections;

public enum CollectionImportEnqueueStatus
{
    Queued,
    AlreadyQueued,
    RecentlyCompleted,
    Unavailable
}

public sealed record CollectionImportEnqueueResult(
    CollectionImportEnqueueStatus Status,
    long? ImportId);

public sealed class CollectionImportService(
    AppDbContext dbContext,
    IOptions<CampOptions> campOptions,
    IOptions<BggOptions> bggOptions)
{
    private static readonly TimeSpan RecentImportWindow = TimeSpan.FromDays(2);

    public async Task<CollectionImportEnqueueResult> EnqueuePersonalAsync(
        long telegramUserId,
        ExternalGameProvider provider,
        string externalUsername,
        CancellationToken cancellationToken)
    {
        if (provider == ExternalGameProvider.Bgg && !bggOptions.Value.IsAvailable)
        {
            return new CollectionImportEnqueueResult(CollectionImportEnqueueStatus.Unavailable, null);
        }

        var participant = await dbContext.Participants.SingleAsync(
            value => value.TelegramUserId == telegramUserId,
            cancellationToken);

        return await EnqueueAsync(
            telegramUserId,
            participant.Id,
            ImportTarget.Participant,
            provider,
            externalUsername,
            cancellationToken);
    }

    public Task<CollectionImportEnqueueResult> EnqueueClubAsync(
        long telegramUserId,
        ExternalGameProvider provider,
        string externalUsername,
        CancellationToken cancellationToken)
    {
        if (!campOptions.Value.AdminTelegramIds.Contains(telegramUserId))
        {
            throw new UnauthorizedAccessException("Импорт коллекции клуба доступен только администратору.");
        }

        if (provider == ExternalGameProvider.Bgg && !bggOptions.Value.IsAvailable)
        {
            return Task.FromResult(
                new CollectionImportEnqueueResult(CollectionImportEnqueueStatus.Unavailable, null));
        }

        return EnqueueAsync(
            telegramUserId,
            null,
            ImportTarget.Club,
            provider,
            externalUsername,
            cancellationToken);
    }

    private async Task<CollectionImportEnqueueResult> EnqueueAsync(
        long requestedByTelegramUserId,
        long? participantId,
        ImportTarget target,
        ExternalGameProvider provider,
        string externalUsername,
        CancellationToken cancellationToken)
    {
        var username = externalUsername.Trim();
        var normalizedUsername = username.ToLowerInvariant();
        var recentCutoff = DateTimeOffset.UtcNow - RecentImportWindow;

        var baseQuery = dbContext.CollectionImports.Where(value =>
            value.Provider == provider
            && value.Target == target
            && value.ParticipantId == participantId
            && value.ExternalUsername.ToLower() == normalizedUsername);

        var active = await baseQuery
            .Where(value => value.Status == ImportStatus.Pending || value.Status == ImportStatus.Running)
            .OrderByDescending(value => value.CreatedAt)
            .Select(value => (long?)value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (active.HasValue)
        {
            return new CollectionImportEnqueueResult(
                CollectionImportEnqueueStatus.AlreadyQueued,
                active.Value);
        }

        var recent = await baseQuery
            .Where(value => value.Status == ImportStatus.Completed
                && value.CompletedAt >= recentCutoff)
            .OrderByDescending(value => value.CompletedAt)
            .Select(value => (long?)value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (recent.HasValue)
        {
            return new CollectionImportEnqueueResult(
                CollectionImportEnqueueStatus.RecentlyCompleted,
                recent.Value);
        }

        var now = DateTimeOffset.UtcNow;
        var collectionImport = new CollectionImport
        {
            ParticipantId = participantId,
            RequestedByTelegramUserId = requestedByTelegramUserId,
            Target = target,
            Provider = provider,
            ExternalUsername = username,
            Status = ImportStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.CollectionImports.Add(collectionImport);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CollectionImportEnqueueResult(
            CollectionImportEnqueueStatus.Queued,
            collectionImport.Id);
    }
}
