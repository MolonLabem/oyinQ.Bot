using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.MiniApp;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record CreateGatheringCommand(string CommunityKey, string GameSource, long BggId,
    IReadOnlyCollection<long> SelectedExpansionIds, DateTimeOffset StartsAt, int MinimumPlayers,
    int DesiredPlayers, int MaximumPlayers, string? Description, bool CanTeachRules);

public sealed record UpdateGatheringCommand(DateTimeOffset StartsAt, int MinimumPlayers,
    int DesiredPlayers, int MaximumPlayers, string? Description, bool CanTeachRules,
    IReadOnlyCollection<long> SelectedExpansionIds);

public sealed class GatheringManagementService(
    AppDbContext dbContext,
    GatheringGameSelectionService gameSelection,
    CampParticipationPolicy participationPolicy,
    GatheringNotificationService notifications,
    TimeProvider timeProvider)
{
    public async Task<GameGathering> CreateAsync(BotCommunity community, TelegramMiniAppIdentity identity,
        CreateGatheringCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        GatheringRules.EnsureFutureStart(command.StartsAt, now);
        await EnsureCommunityMutationAllowedAsync(community, identity.TelegramUserId,
            command.StartsAt, cancellationToken);
        var participant = await GetOrCreateParticipantAsync(identity, community.Key, cancellationToken);
        var snapshot = string.Equals(command.GameSource, "bgg", StringComparison.OrdinalIgnoreCase)
            ? await gameSelection.FromArbitraryBggAsync(command.BggId, command.SelectedExpansionIds, cancellationToken)
            : community.Mode == BotMode.Club
                ? await gameSelection.FromClubCollectionAsync(community.Key, command.BggId,
                    command.SelectedExpansionIds, cancellationToken)
                : await gameSelection.FromCampCatalogAsync(community.Key, command.BggId,
                    command.SelectedExpansionIds, cancellationToken);
        ValidateGamePlayerLimits(snapshot, command.MinimumPlayers, command.MaximumPlayers);
        var gathering = GatheringRules.Create(community.Key, snapshot, participant.Id,
            command.StartsAt, command.MinimumPlayers, command.DesiredPlayers, command.MaximumPlayers,
            command.Description, command.CanTeachRules, now);
        dbContext.GameGatherings.Add(gathering);
        await dbContext.SaveChangesAsync(cancellationToken);
        return gathering;
    }

    public async Task<GameGathering> UpdateAsync(Guid publicId, string communityKey, long telegramUserId,
        UpdateGatheringCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        GatheringRules.EnsureFutureStart(command.StartsAt, now);
        var gathering = await RequireManagedAsync(publicId, communityKey, telegramUserId, cancellationToken);
        var timeChanged = gathering.StartsAtUtc != command.StartsAt.ToUniversalTime();
        var community = await dbContext.OyinQCommunities.AsNoTracking().SingleAsync(x => x.Key == communityKey,
            cancellationToken);
        await EnsureCommunityMutationAllowedAsync(community.ToBotCommunity(), telegramUserId,
            command.StartsAt, cancellationToken);
        ValidateGamePlayerLimits(GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson),
            command.MinimumPlayers, command.MaximumPlayers);
        GatheringRules.Update(gathering, command.StartsAt, command.MinimumPlayers, command.DesiredPlayers,
            command.MaximumPlayers, command.Description, command.CanTeachRules,
            command.SelectedExpansionIds, now);
        gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (timeChanged) await notifications.NotifyTimeChangedAsync(publicId, cancellationToken);
        return gathering;
    }

    private static void ValidateGamePlayerLimits(GatheringGameSnapshot snapshot, int minimum, int maximum)
    {
        var gameRange = PlayerCountRange.Normalize(snapshot.MinPlayers, snapshot.MaxPlayers);
        if (minimum < gameRange.Minimum)
            throw new InvalidOperationException($"Для «{snapshot.Name}» минимум игроков — {gameRange.Minimum}.");
        if (maximum > gameRange.Maximum)
            throw new InvalidOperationException($"Для «{snapshot.Name}» максимум игроков — {gameRange.Maximum}.");
    }

    public async Task<GameGathering> ChangeLifecycleAsync(Guid publicId, string communityKey,
        long telegramUserId, string action, string? reason, CancellationToken cancellationToken)
    {
        var gathering = await RequireManagedAsync(publicId, communityKey, telegramUserId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var cancelled = false;
        switch (action.ToLowerInvariant())
        {
            case "close": GatheringRules.Close(gathering, now); break;
            case "reopen": GatheringRules.Reopen(gathering, now); break;
            case "cancel": GatheringRules.Cancel(gathering, reason, now); cancelled = true; break;
            default: throw new InvalidOperationException("Неизвестное действие со сбором.");
        }
        gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (cancelled) await notifications.NotifyCancellationAsync(publicId, cancellationToken);
        return gathering;
    }

    private async Task<GameGathering> RequireManagedAsync(Guid publicId, string communityKey,
        long telegramUserId, CancellationToken cancellationToken)
    {
        var gathering = await dbContext.GameGatherings
            .Include(x => x.OrganizerParticipant).Include(x => x.Participants).ThenInclude(x => x.Participant)
            .Include(x => x.Expansions).Include(x => x.Guests)
            .SingleOrDefaultAsync(x => x.PublicId == publicId && x.CommunityKey == communityKey,
                cancellationToken) ?? throw new KeyNotFoundException("Сбор не найден.");
        GatheringAccessPolicy.RequireOrganizer(gathering, telegramUserId);
        return gathering;
    }

    private async Task EnsureCommunityMutationAllowedAsync(BotCommunity community, long telegramUserId,
        DateTimeOffset startsAt, CancellationToken cancellationToken)
    {
        if (community.Mode != BotMode.Camp) return;
        var camp = await dbContext.Camps.AsNoTracking().Include(x => x.BotChat)
            .SingleAsync(x => x.BotChatKey == community.Key, cancellationToken);
        var participantId = await dbContext.Participants.Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (participantId is null)
            throw new UnauthorizedAccessException("Сначала завершите регистрацию в кэмпе.");
        var localDate = CampRules.GetLocalGatheringDate(startsAt, community.TimeZoneId);
        CampRules.EnsureGatheringDateWithinRange(camp, localDate);
        await participationPolicy.RequireCompleteRegistrationAsync(camp.Id, participantId.Value,
            cancellationToken, localDate);
    }

    private async Task<Participant> GetOrCreateParticipantAsync(TelegramMiniAppIdentity identity,
        string communityKey, CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            x => x.TelegramUserId == identity.TelegramUserId, cancellationToken);
        if (participant is not null)
        {
            var refreshTime = timeProvider.GetUtcNow();
            ParticipantIdentityPolicy.RefreshTrustedPresentation(participant, identity.TelegramUsername,
                identity.DisplayName, refreshTime);
            participant.ActiveCommunityKey = communityKey;
            participant.UpdatedAt = refreshTime;
            await dbContext.SaveChangesAsync(cancellationToken);
            return participant;
        }
        var now = timeProvider.GetUtcNow();
        participant = ParticipantIdentityPolicy.Create(identity.TelegramUserId,
            identity.TelegramUsername, identity.DisplayName, now);
        participant.ActiveCommunityKey = communityKey;
        dbContext.Participants.Add(participant);
        await dbContext.SaveChangesAsync(cancellationToken);
        return participant;
    }
}
