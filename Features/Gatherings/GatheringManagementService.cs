using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.MiniApp;

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
    TimeProvider timeProvider)
{
    public async Task<GameGathering> CreateAsync(BotCommunity community, TelegramMiniAppIdentity identity,
        CreateGatheringCommand command, CancellationToken cancellationToken)
    {
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
        var gathering = GatheringRules.Create(community.Key, snapshot, participant.Id,
            command.StartsAt, command.MinimumPlayers, command.DesiredPlayers, command.MaximumPlayers,
            command.Description, command.CanTeachRules, timeProvider.GetUtcNow());
        dbContext.GameGatherings.Add(gathering);
        await dbContext.SaveChangesAsync(cancellationToken);
        return gathering;
    }

    public async Task<GameGathering> UpdateAsync(Guid publicId, string communityKey, long telegramUserId,
        UpdateGatheringCommand command, CancellationToken cancellationToken)
    {
        var gathering = await RequireManagedAsync(publicId, communityKey, telegramUserId, cancellationToken);
        var community = await dbContext.OyinQCommunities.AsNoTracking().SingleAsync(x => x.Key == communityKey,
            cancellationToken);
        await EnsureCommunityMutationAllowedAsync(community.ToBotCommunity(), telegramUserId,
            command.StartsAt, cancellationToken);
        GatheringRules.Update(gathering, command.StartsAt, command.MinimumPlayers, command.DesiredPlayers,
            command.MaximumPlayers, command.Description, command.CanTeachRules,
            command.SelectedExpansionIds, timeProvider.GetUtcNow());
        gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        await dbContext.SaveChangesAsync(cancellationToken);
        return gathering;
    }

    public async Task<GameGathering> ChangeLifecycleAsync(Guid publicId, string communityKey,
        long telegramUserId, string action, string? reason, CancellationToken cancellationToken)
    {
        var gathering = await RequireManagedAsync(publicId, communityKey, telegramUserId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        switch (action.ToLowerInvariant())
        {
            case "close": GatheringRules.Close(gathering, now); break;
            case "reopen": GatheringRules.Reopen(gathering, now); break;
            case "cancel": GatheringRules.Cancel(gathering, reason, now); break;
            default: throw new InvalidOperationException("Неизвестное действие со сбором.");
        }
        gathering.PublicationStatus = GatheringPublicationStatus.Pending;
        await dbContext.SaveChangesAsync(cancellationToken);
        return gathering;
    }

    private async Task<GameGathering> RequireManagedAsync(Guid publicId, string communityKey,
        long telegramUserId, CancellationToken cancellationToken)
    {
        var gathering = await dbContext.GameGatherings
            .Include(x => x.OrganizerParticipant).Include(x => x.Participants).ThenInclude(x => x.Participant)
            .Include(x => x.Expansions)
            .SingleOrDefaultAsync(x => x.PublicId == publicId && x.CommunityKey == communityKey,
                cancellationToken) ?? throw new KeyNotFoundException("Сбор не найден.");
        if (gathering.OrganizerParticipant.TelegramUserId != telegramUserId)
            throw new UnauthorizedAccessException("Управлять сбором может только организатор.");
        return gathering;
    }

    private async Task EnsureCommunityMutationAllowedAsync(BotCommunity community, long telegramUserId,
        DateTimeOffset startsAt, CancellationToken cancellationToken)
    {
        if (community.Mode != BotMode.Camp) return;
        var camp = await dbContext.Camps.AsNoTracking()
            .SingleAsync(x => x.BotChatKey == community.Key, cancellationToken);
        if (camp.Status != CampStatus.Active) throw new InvalidOperationException("Кэмп не принимает новые сборы.");
        var participantId = await dbContext.Participants.Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (participantId is null || !await dbContext.CampRegistrations.AnyAsync(
                x => x.CampId == camp.Id && x.ParticipantId == participantId, cancellationToken))
            throw new UnauthorizedAccessException("Сначала завершите регистрацию в кэмпе.");
        if (camp.StartDate is not { } start || camp.EndDate is not { } end)
            throw new InvalidOperationException("Для кэмпа ещё не настроены даты.");
        var local = TimeZoneInfo.ConvertTime(startsAt, TimeZoneInfo.FindSystemTimeZoneById(community.TimeZoneId));
        var localDate = DateOnly.FromDateTime(local.DateTime);
        if (localDate < start || localDate > end)
            throw new InvalidOperationException("Сбор должен проходить в пределах дат кэмпа.");
    }

    private async Task<Participant> GetOrCreateParticipantAsync(TelegramMiniAppIdentity identity,
        string communityKey, CancellationToken cancellationToken)
    {
        var participant = await dbContext.Participants.SingleOrDefaultAsync(
            x => x.TelegramUserId == identity.TelegramUserId, cancellationToken);
        if (participant is not null) return participant;
        var now = timeProvider.GetUtcNow();
        participant = new Participant
        {
            TelegramUserId = identity.TelegramUserId, TelegramUsername = identity.TelegramUsername,
            DisplayName = identity.DisplayName ?? $"Telegram {identity.TelegramUserId}",
            ActiveCommunityKey = communityKey, CreatedAt = now, UpdatedAt = now
        };
        dbContext.Participants.Add(participant);
        await dbContext.SaveChangesAsync(cancellationToken);
        return participant;
    }
}
