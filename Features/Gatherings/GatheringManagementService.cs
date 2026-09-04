using System.Data;
using oyinQ.Bot.Features.Collections;
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
    int DesiredPlayers, int MaximumPlayers, string? Description, bool CanTeachRules, bool ConfirmScheduleConflict = false, bool AddToCollection = false, bool BringToCamp = false);

public sealed record UpdateGatheringCommand(DateTimeOffset StartsAt, int MinimumPlayers,
    int DesiredPlayers, int MaximumPlayers, string? Description, bool CanTeachRules,
    IReadOnlyCollection<long> SelectedExpansionIds, bool ConfirmScheduleConflict = false);
public sealed record GatheringUpdateResult(GameGathering Gathering,
    IReadOnlyList<GatheringPromotion> Promotions);

public sealed class GatheringManagementService(
    AppDbContext dbContext,
    GatheringGameSelectionService gameSelection,
    CampParticipationPolicy participationPolicy,
    GatheringNotificationService notifications,
    TimeProvider timeProvider, GatheringScheduleConflictService? conflicts = null)
{
    public async Task<GameGathering> CreateAsync(BotCommunity community, TelegramMiniAppIdentity identity,
        CreateGatheringCommand command, CancellationToken cancellationToken)
    {
        var participant = await GetOrCreateParticipantAsync(identity, community.Key, cancellationToken);
        // Reject an invalid Camp selection cheaply; the same policy is authoritative again under the lock below.
        await EnsureCommunityMutationAllowedAsync(community, identity.TelegramUserId, command.StartsAt, cancellationToken);
        var external = string.Equals(command.GameSource, "bgg", StringComparison.OrdinalIgnoreCase);
        if (command.AddToCollection && !external) throw new ArgumentException("Добавление при создании доступно для выбранной игры BGG.");
        if (command.BringToCamp && community.Mode != BotMode.Camp) throw new ArgumentException("Отметка кэмпа доступна только в кэмпе.");
        var selection = external ? await gameSelection.ExternalSelectionAsync(command.BggId, command.SelectedExpansionIds, cancellationToken) : default;
        var snapshot = external
            ? selection.Snapshot
            : community.Mode == BotMode.Club
                ? await gameSelection.FromClubCollectionAsync(community.Key, command.BggId,
                    command.SelectedExpansionIds, cancellationToken, identity.TelegramUserId)
                : await gameSelection.FromCampCatalogAsync(community.Key, command.BggId,
                    command.SelectedExpansionIds, cancellationToken, identity.TelegramUserId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var authoritative = await CommunityMutationLock.AcquireAsync(dbContext, community.Key, cancellationToken);
        if (!authoritative.IsActive || authoritative.DeletedAt is not null || authoritative.Mode != community.Mode)
            throw new InvalidOperationException("Сообщество больше не принимает новые сборы.");
        community = authoritative.ToBotCommunity();
        // Camp -> participant is the same lock order used by registration/contribution mutations.
        var campId = community.Mode == BotMode.Camp ? await dbContext.Camps.Where(x => x.BotChatKey == community.Key).Select(x => (long?)x.Id).SingleAsync(cancellationToken) : null;
        if (campId is { } lockedCampId && dbContext.Database.IsRelational())
            await dbContext.Camps.FromSqlInterpolated($"SELECT * FROM \"Camps\" WHERE \"Id\" = {lockedCampId} FOR UPDATE").SingleAsync(cancellationToken);
        await EnsureCommunityMutationAllowedAsync(community, identity.TelegramUserId, command.StartsAt, cancellationToken);
        await (conflicts ?? new GatheringScheduleConflictService(dbContext)).WarnAsync(participant.Id, command.StartsAt, null,
            command.ConfirmScheduleConflict, timeProvider.GetUtcNow(), cancellationToken);
        var now = timeProvider.GetUtcNow();
        ValidateGamePlayerLimits(snapshot, command.MinimumPlayers, command.MaximumPlayers);
        var gathering = GatheringRules.Create(community.Key, snapshot, participant.Id,
            command.StartsAt, command.MinimumPlayers, command.DesiredPlayers, command.MaximumPlayers,
            command.Description, command.CanTeachRules, now);
        if (command.AddToCollection)
            await new ParticipantCollectionService(dbContext).UpsertAsync(participant.Id, selection.Ownership.ToArray(), CollectionItemSource.Manual, now, cancellationToken, preserveExisting: true);
        if (command.BringToCamp && campId is { } targetCampId)
        {
            var contributions = new CampContributionSelectionService(dbContext, participationPolicy, timeProvider);
            await contributions.SetCommitmentAsync(targetCampId, participant.Id, command.BggId, CollectionItemType.BaseGame,
                CampBringCommitment.Bringing, cancellationToken, command.StartsAt);
            foreach (var expansionId in command.SelectedExpansionIds.Distinct())
                await contributions.SetCommitmentAsync(targetCampId, participant.Id, expansionId, CollectionItemType.Expansion,
                    CampBringCommitment.Bringing, cancellationToken, command.StartsAt);
        }
        dbContext.GameGatherings.Add(gathering);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifications.NotifyWishlistAsync(gathering, cancellationToken);
        await notifications.NotifyFullAsync(gathering.PublicId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return gathering;
    }

    public async Task<GatheringUpdateResult> UpdateAsync(Guid publicId, string communityKey, long telegramUserId,
        UpdateGatheringCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        GatheringRules.EnsureFutureStart(command.StartsAt, now);
        GameGathering gathering;
        IReadOnlyList<GameGatheringParticipant> promoted;
        bool timeChanged;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken))
        {
            gathering = await RequireManagedAsync(publicId, communityKey, telegramUserId, cancellationToken);
            timeChanged = gathering.StartsAtUtc != command.StartsAt.ToUniversalTime();
            if (timeChanged) await (conflicts ?? new GatheringScheduleConflictService(dbContext)).WarnAsync(gathering.OrganizerParticipantId, command.StartsAt, publicId, command.ConfirmScheduleConflict, now, cancellationToken);
            var community = await dbContext.OyinQCommunities.AsNoTracking()
                .SingleAsync(x => x.Key == communityKey, cancellationToken);
            await EnsureCommunityMutationAllowedAsync(community.ToBotCommunity(), telegramUserId,
                command.StartsAt, cancellationToken);
            ValidateGamePlayerLimits(GatheringGameSnapshotSerializer.Deserialize(gathering.GameSnapshotJson),
                command.MinimumPlayers, command.MaximumPlayers);
            var beforeDetails = (gathering.MinimumPlayers, gathering.DesiredPlayers, gathering.MaximumPlayers,
                gathering.Description, gathering.CanTeachRules, gathering.GameSnapshotJson);
            promoted = GatheringRules.Update(gathering, command.StartsAt, command.MinimumPlayers,
                command.DesiredPlayers, command.MaximumPlayers, command.Description, command.CanTeachRules,
                command.SelectedExpansionIds, now);
            gathering.PublicationStatus = GatheringPublicationStatus.Pending;
            await dbContext.SaveChangesAsync(cancellationToken);
            if (timeChanged) await notifications.NotifyTimeChangedAsync(publicId, cancellationToken);
            else if (beforeDetails != (gathering.MinimumPlayers, gathering.DesiredPlayers, gathering.MaximumPlayers,
                gathering.Description, gathering.CanTeachRules, gathering.GameSnapshotJson))
                await notifications.NotifyDetailsChangedAsync(publicId, cancellationToken);
            await notifications.NotifyPromotionsAsync(communityKey, publicId, promoted.Select(x => GatheringPromotion.Capture(x.Participant)), cancellationToken);
            await notifications.NotifyFullAsync(publicId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return new(gathering, promoted.Select(x => GatheringPromotion.Capture(x.Participant)).ToArray());
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
        GameGathering gathering;
        var now = timeProvider.GetUtcNow();
        var cancelled = false;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken))
        {
            gathering = await RequireManagedAsync(publicId, communityKey, telegramUserId, cancellationToken);
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
            await notifications.NotifyFullAsync(publicId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return gathering;
    }

    private async Task<GameGathering> RequireManagedAsync(Guid publicId, string communityKey,
        long telegramUserId, CancellationToken cancellationToken)
    {
        var gathering = await GatheringWriteStore.LockAsync(dbContext, publicId, communityKey,
            cancellationToken);
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
        CampOperatingWindow.RequireContains(camp, startsAt);
        await participationPolicy.RequireCompleteRegistrationAsync(camp.Id, participantId.Value,
            cancellationToken, localDate);
    }

    private Task<Participant> GetOrCreateParticipantAsync(TelegramMiniAppIdentity identity,
        string communityKey, CancellationToken cancellationToken) =>
        new ParticipantIdentityService(dbContext, timeProvider).GetOrCreateAsync(
            identity.TelegramUserId, identity.TelegramUsername, identity.DisplayName, communityKey, cancellationToken);
}
