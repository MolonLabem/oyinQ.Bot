using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Features.Admin;

public sealed record CommunityDeletionResult(string CommunityKey, string Name,
    IReadOnlyList<Guid> CancelledGatheringIds, bool AlreadyDeleted);

public sealed class CommunityDeletionService(
    AppDbContext dbContext,
    IAdminAuthorizationService authorization,
    TimeProvider timeProvider)
{
    public async Task<CommunityDeletionResult> DeleteClubAsync(long actorTelegramUserId, long clubId,
        CancellationToken cancellationToken)
    {
        EnsureSuperAdmin(actorTelegramUserId);
        var club = await dbContext.Clubs.Include(x => x.BotChat)
            .SingleOrDefaultAsync(x => x.Id == clubId, cancellationToken)
            ?? throw new KeyNotFoundException("Клуб не найден.");
        if (club.BotChat.DeletedAt is not null) return await DeleteAsync(club.BotChat, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var jobs = await dbContext.ClubMetadataRefreshes.Where(x => x.ClubId == clubId
                && (x.Status == ClubMetadataRefreshStatus.Queued || x.Status == ClubMetadataRefreshStatus.Running))
            .ToArrayAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.Status = ClubMetadataRefreshStatus.Failed;
            job.Error = "Клуб удалён из OyinQ.";
            job.LeaseId = null;
            job.LeaseExpiresAt = null;
            job.UpdatedAt = now;
        }
        var imports = await dbContext.ClubBggImports.Where(x => x.ClubId == clubId
                && (x.Status == ClubBggImportStatus.Queued || x.Status == ClubBggImportStatus.Running))
            .ToArrayAsync(cancellationToken);
        foreach (var import in imports)
        {
            import.Status = ClubBggImportStatus.Failed;
            import.Error = "Клуб удалён из OyinQ.";
            import.LeaseId = null;
            import.LeaseExpiresAt = null;
            import.UpdatedAt = now;
        }
        return await DeleteAsync(club.BotChat, cancellationToken);
    }

    public async Task<CommunityDeletionResult> DeleteCampAsync(long actorTelegramUserId, long campId,
        CancellationToken cancellationToken)
    {
        EnsureSuperAdmin(actorTelegramUserId);
        var camp = await dbContext.Camps.Include(x => x.BotChat)
            .SingleOrDefaultAsync(x => x.Id == campId, cancellationToken)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        if (camp.BotChat.DeletedAt is not null) return await DeleteAsync(camp.BotChat, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var imports = await dbContext.CampBggImports.Where(x => x.CampId == campId
                && (x.Status == CampBggImportStatus.Queued || x.Status == CampBggImportStatus.Running))
            .ToArrayAsync(cancellationToken);
        foreach (var import in imports)
        {
            import.Status = CampBggImportStatus.Cancelled;
            import.CancellationRequestedAt = now;
            import.Error = "Кэмп удалён из OyinQ.";
            import.LeaseId = null;
            import.LeaseExpiresAt = null;
            import.UpdatedAt = now;
        }
        return await DeleteAsync(camp.BotChat, cancellationToken);
    }

    private async Task<CommunityDeletionResult> DeleteAsync(OyinQCommunity community,
        CancellationToken cancellationToken)
    {
        if (community.DeletedAt is not null)
        {
            var pending = await dbContext.GameGatherings.AsNoTracking()
                .Where(x => x.CommunityKey == community.Key && x.Status == GatheringStatus.Cancelled
                    && x.PublicationStatus == GatheringPublicationStatus.Pending)
                .Select(x => x.PublicId).ToArrayAsync(cancellationToken);
            return new(community.Key, community.Name, pending, true);
        }

        var now = timeProvider.GetUtcNow();
        var activeStatuses = new[] { GatheringStatus.Recruiting, GatheringStatus.Ready,
            GatheringStatus.Full, GatheringStatus.Closed };
        var future = await dbContext.GameGatherings
            .Where(x => x.CommunityKey == community.Key && x.StartsAtUtc > now
                && activeStatuses.Contains(x.Status))
            .Include(x => x.Participants)
            .ToArrayAsync(cancellationToken);
        foreach (var gathering in future)
        {
            GatheringRules.Cancel(gathering, "Сообщество удалено из OyinQ", now);
            gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        }

        var permissions = await dbContext.ChatAdminPermissions
            .Where(x => x.CommunityKey == community.Key && x.RevokedAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var permission in permissions) permission.RevokedAt = now;

        community.IsActive = false;
        community.DeletedAt = now;
        community.PostingMessageThreadId = null;
        community.PostingTopicInvalidatedAt = now;
        community.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(community.Key, community.Name, future.Select(x => x.PublicId).ToArray(), false);
    }

    private void EnsureSuperAdmin(long actorTelegramUserId)
    {
        if (!authorization.IsSuperAdmin(actorTelegramUserId))
            throw new UnauthorizedAccessException("Удалять клубы и кэмпы может только Super Admin.");
    }
}
