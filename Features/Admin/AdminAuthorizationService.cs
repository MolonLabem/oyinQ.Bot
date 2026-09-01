using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Admin;

public sealed record AdminChatAccess(
    string? CommunityKey,
    string Name,
    long TelegramChatId,
    BotMode Mode,
    bool IsActive,
    bool IsApproved,
    bool IsSuperAdmin);

public sealed record GroupAdministratorRecord(
    long TelegramUserId,
    string? DisplayName,
    string? TelegramUsername,
    long GrantedByTelegramUserId,
    DateTimeOffset CreatedAt);

public sealed record EligibleGroupAdministrator(
    long TelegramUserId,
    string? DisplayName,
    string? TelegramUsername);

public interface ITelegramChatAdministratorVerifier
{
    Task<bool> IsAdministratorAsync(long telegramChatId, long telegramUserId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<EligibleGroupAdministrator>> GetAdministratorsAsync(long telegramChatId,
        CancellationToken cancellationToken);
}

public interface IAdminAuthorizationService
{
    bool IsSuperAdmin(long telegramUserId);
    Task<bool> CanOpenAdminPanelAsync(long telegramUserId, CancellationToken cancellationToken);
    Task<bool> CanAdministerCommunityAsync(long telegramUserId, string communityKey,
        CancellationToken cancellationToken);
    Task<bool> CanAdministerClubAsync(long telegramUserId, long clubId, CancellationToken cancellationToken);
    Task<bool> CanAdministerCampAsync(long telegramUserId, long campId, CancellationToken cancellationToken);
    Task<bool> CanManageAdminsAsync(long telegramUserId, string communityKey,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminChatAccess>> GetAdminPanelChatsAsync(long telegramUserId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<GroupAdministratorRecord>> ListGroupAdminsAsync(long actorTelegramUserId,
        string communityKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<EligibleGroupAdministrator>> ListEligibleGroupAdminsAsync(long actorTelegramUserId,
        string communityKey, CancellationToken cancellationToken);
    Task GrantEligibleGroupAdminAsync(long actorTelegramUserId, string communityKey,
        long targetTelegramUserId, CancellationToken cancellationToken);
    Task GrantGroupAdminAsync(long actorTelegramUserId, string communityKey, long targetTelegramUserId,
        string? displayName, string? telegramUsername, CancellationToken cancellationToken);
    Task RevokeGroupAdminAsync(long actorTelegramUserId, string communityKey, long targetTelegramUserId,
        CancellationToken cancellationToken);
}

public sealed class AdminAuthorizationService(
    AppDbContext dbContext,
    ITelegramChatAdministratorVerifier telegramVerifier,
    IOptions<AdministrationOptions> options,
    TimeProvider timeProvider) : IAdminAuthorizationService
{
    public bool IsSuperAdmin(long telegramUserId) =>
        telegramUserId > 0 && options.Value.SuperAdminTelegramUserIds.Contains(telegramUserId);

    public async Task<bool> CanOpenAdminPanelAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        if (IsSuperAdmin(telegramUserId)) return true;
        var chats = await KnownChatIdsAsync(cancellationToken);
        foreach (var chatId in chats)
            if (await telegramVerifier.IsAdministratorAsync(chatId, telegramUserId, cancellationToken)) return true;
        return false;
    }

    public async Task<bool> CanAdministerCommunityAsync(long telegramUserId, string communityKey,
        CancellationToken cancellationToken)
    {
        if (IsSuperAdmin(telegramUserId))
            return await dbContext.OyinQCommunities.AsNoTracking().AnyAsync(x => x.Key == communityKey,
                cancellationToken);

        var target = await dbContext.OyinQCommunities.AsNoTracking()
            .Where(x => x.Key == communityKey)
            .Select(x => new
            {
                x.TelegramChatId,
                Approved = x.AdminPermissions.Any(p => p.TelegramUserId == telegramUserId && p.RevokedAt == null)
            })
            .SingleOrDefaultAsync(cancellationToken);
        return target is not null && target.Approved
            && await telegramVerifier.IsAdministratorAsync(target.TelegramChatId, telegramUserId, cancellationToken);
    }

    public Task<bool> CanManageAdminsAsync(long telegramUserId, string communityKey,
        CancellationToken cancellationToken) =>
        CanAdministerCommunityAsync(telegramUserId, communityKey, cancellationToken);

    public async Task<bool> CanAdministerClubAsync(long telegramUserId, long clubId,
        CancellationToken cancellationToken)
    {
        var key = await dbContext.Clubs.AsNoTracking().Where(x => x.Id == clubId)
            .Select(x => x.BotChatKey).SingleOrDefaultAsync(cancellationToken);
        return key is not null && await CanAdministerCommunityAsync(telegramUserId, key, cancellationToken);
    }

    public async Task<bool> CanAdministerCampAsync(long telegramUserId, long campId,
        CancellationToken cancellationToken)
    {
        var key = await dbContext.Camps.AsNoTracking().Where(x => x.Id == campId)
            .Select(x => x.BotChatKey).SingleOrDefaultAsync(cancellationToken);
        return key is not null && await CanAdministerCommunityAsync(telegramUserId, key, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminChatAccess>> GetAdminPanelChatsAsync(long telegramUserId,
        CancellationToken cancellationToken)
    {
        var superAdmin = IsSuperAdmin(telegramUserId);
        var chats = await dbContext.OyinQCommunities.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Key, x.Name, x.TelegramChatId, x.Mode, x.IsActive,
                Approved = x.AdminPermissions.Any(p => p.TelegramUserId == telegramUserId && p.RevokedAt == null)
            })
            .ToArrayAsync(cancellationToken);
        var result = new List<AdminChatAccess>();
        var configuredIds = chats.Select(x => x.TelegramChatId).ToHashSet();
        var knownPresence = await dbContext.KnownTelegramChats.AsNoTracking()
            .Where(x => configuredIds.Contains(x.TelegramChatId))
            .ToDictionaryAsync(x => x.TelegramChatId, x => x.IsBotPresent, cancellationToken);
        foreach (var chat in chats.Where(chat => !knownPresence.TryGetValue(chat.TelegramChatId, out var present)
                     || present))
        {
            if (superAdmin)
            {
                result.Add(new(chat.Key, chat.Name, chat.TelegramChatId, chat.Mode, chat.IsActive,
                    true, true));
                continue;
            }
            if (!await telegramVerifier.IsAdministratorAsync(chat.TelegramChatId, telegramUserId,
                    cancellationToken)) continue;
            result.Add(new(chat.Key, chat.Name, chat.TelegramChatId, chat.Mode, chat.IsActive,
                chat.Approved, false));
        }
        var unconfigured = await dbContext.KnownTelegramChats.AsNoTracking()
            .Where(x => x.IsBotPresent && !configuredIds.Contains(x.TelegramChatId))
            .OrderBy(x => x.Title).ToArrayAsync(cancellationToken);
        foreach (var chat in unconfigured)
        {
            if (!superAdmin && !await telegramVerifier.IsAdministratorAsync(chat.TelegramChatId,
                    telegramUserId, cancellationToken)) continue;
            result.Add(new(null, chat.Title ?? $"Telegram {chat.TelegramChatId}", chat.TelegramChatId,
                BotMode.Club, false, false, superAdmin));
        }
        return result;
    }

    public async Task<IReadOnlyList<GroupAdministratorRecord>> ListGroupAdminsAsync(long actorTelegramUserId,
        string communityKey, CancellationToken cancellationToken)
    {
        await EnsureCanManageAsync(actorTelegramUserId, communityKey, cancellationToken);
        return await dbContext.ChatAdminPermissions.AsNoTracking()
            .Where(x => x.CommunityKey == communityKey && x.RevokedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new GroupAdministratorRecord(x.TelegramUserId, x.DisplayName, x.TelegramUsername,
                x.GrantedByTelegramUserId, x.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EligibleGroupAdministrator>> ListEligibleGroupAdminsAsync(
        long actorTelegramUserId, string communityKey, CancellationToken cancellationToken)
    {
        await EnsureCanManageAsync(actorTelegramUserId, communityKey, cancellationToken);
        var target = await dbContext.OyinQCommunities.AsNoTracking()
            .Where(x => x.Key == communityKey)
            .Select(x => new
            {
                x.TelegramChatId,
                ApprovedIds = x.AdminPermissions.Where(p => p.RevokedAt == null)
                    .Select(p => p.TelegramUserId).ToArray()
            }).SingleAsync(cancellationToken);
        var approvedIds = target.ApprovedIds.ToHashSet();
        return (await telegramVerifier.GetAdministratorsAsync(target.TelegramChatId, cancellationToken))
            .Where(x => !approvedIds.Contains(x.TelegramUserId))
            .OrderBy(x => x.DisplayName ?? x.TelegramUsername ?? x.TelegramUserId.ToString())
            .ToArray();
    }

    public async Task GrantEligibleGroupAdminAsync(long actorTelegramUserId, string communityKey,
        long targetTelegramUserId, CancellationToken cancellationToken)
    {
        var candidate = (await ListEligibleGroupAdminsAsync(actorTelegramUserId, communityKey,
                cancellationToken)).SingleOrDefault(x => x.TelegramUserId == targetTelegramUserId)
            ?? throw new InvalidOperationException(
                "Пользователь должен быть действующим администратором этого чата Telegram.");
        await GrantGroupAdminAsync(actorTelegramUserId, communityKey, candidate.TelegramUserId,
            candidate.DisplayName, candidate.TelegramUsername, cancellationToken);
    }

    public async Task GrantGroupAdminAsync(long actorTelegramUserId, string communityKey,
        long targetTelegramUserId, string? displayName, string? telegramUsername,
        CancellationToken cancellationToken)
    {
        await EnsureCanManageAsync(actorTelegramUserId, communityKey, cancellationToken);
        if (targetTelegramUserId <= 0) throw new InvalidOperationException("Telegram ID должен быть положительным.");
        var chatId = await dbContext.OyinQCommunities.Where(x => x.Key == communityKey)
            .Select(x => x.TelegramChatId).SingleAsync(cancellationToken);
        if (!await telegramVerifier.IsAdministratorAsync(chatId, targetTelegramUserId, cancellationToken))
            throw new InvalidOperationException("Пользователь должен быть действующим администратором этого чата Telegram.");

        var now = timeProvider.GetUtcNow();
        var permission = await dbContext.ChatAdminPermissions.SingleOrDefaultAsync(
            x => x.CommunityKey == communityKey && x.TelegramUserId == targetTelegramUserId,
            cancellationToken);
        if (permission is null)
        {
            permission = new ChatAdminPermission
            {
                CommunityKey = communityKey,
                TelegramUserId = targetTelegramUserId,
                CreatedAt = now
            };
            dbContext.ChatAdminPermissions.Add(permission);
        }
        permission.DisplayName = Normalize(displayName);
        permission.TelegramUsername = Normalize(telegramUsername);
        permission.GrantedByTelegramUserId = actorTelegramUserId;
        permission.CreatedAt = now;
        permission.RevokedAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeGroupAdminAsync(long actorTelegramUserId, string communityKey,
        long targetTelegramUserId, CancellationToken cancellationToken)
    {
        await EnsureCanManageAsync(actorTelegramUserId, communityKey, cancellationToken);
        var permission = await dbContext.ChatAdminPermissions.SingleOrDefaultAsync(
            x => x.CommunityKey == communityKey && x.TelegramUserId == targetTelegramUserId
                && x.RevokedAt == null, cancellationToken);
        if (permission is null) return;
        permission.RevokedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCanManageAsync(long telegramUserId, string communityKey,
        CancellationToken cancellationToken)
    {
        if (!await CanManageAdminsAsync(telegramUserId, communityKey, cancellationToken))
            throw new UnauthorizedAccessException("Нет доступа к управлению администраторами этого чата.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<long[]> KnownChatIdsAsync(CancellationToken cancellationToken)
    {
        var configured = await dbContext.OyinQCommunities.AsNoTracking()
            .Select(x => x.TelegramChatId).ToArrayAsync(cancellationToken);
        var observed = await dbContext.KnownTelegramChats.AsNoTracking()
            .Where(x => x.IsBotPresent).Select(x => x.TelegramChatId).ToArrayAsync(cancellationToken);
        return configured.Concat(observed).Distinct().ToArray();
    }
}
