using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Features.Catalog;

public enum GameProviderState { ClubProvided, ConfirmedParticipantProvider, AvailableParticipantProviders, NoKnownProvider }
public sealed record GameProviderResponse(GameProviderState State, string Summary,
    IReadOnlyList<CampCatalogProvider> Providers, bool CanBring = false, bool IsOwned = false)
{
    public bool IsConfirmed => State is GameProviderState.ClubProvided or GameProviderState.ConfirmedParticipantProvider;
}

public sealed class GameProviderService(AppDbContext db, GameCatalogService catalog,
    EffectiveCampCatalogService campCatalog, CampParticipationPolicy participation,
    CampContributionSelectionService contributions, TimeProvider clock)
{
    public static GameProviderResponse Describe(bool inBase, IReadOnlyList<CampCatalogProvider> providers)
    {
        providers = providers.GroupBy(x => x.ParticipantId).Select(x => x.OrderByDescending(p => p.Commitment).First()).ToArray();
        var bringing = providers.Where(x => x.Commitment == CampBringCommitment.Bringing).ToArray();
        return new(inBase ? GameProviderState.ClubProvided : bringing.Length > 0
                ? GameProviderState.ConfirmedParticipantProvider : providers.Count > 0
                    ? GameProviderState.AvailableParticipantProviders : GameProviderState.NoKnownProvider,
            inBase ? "Есть в клубе" : bringing.Length > 0
                ? string.Join(", ", bringing.Select(x => x.DisplayName)) + " — привезут"
                : providers.Count > 0 ? "Можно попросить: " + string.Join(", ", providers.Select(x => x.DisplayName))
                : "Никто пока не подтвердил коробку", providers);
    }

    public async Task<GameProviderResponse> ForGameAsync(string key, long bggId, long participantId,
        DateTimeOffset? startsAt, CancellationToken ct)
    {
        var community = await db.OyinQCommunities.AsNoTracking().SingleAsync(x => x.Key == key, ct);
        var date = startsAt is { } instant ? CommunityTime.LocalDate(instant, community.TimeZoneId) : (DateOnly?)null;
        return Evaluate(await LoadContext(community, participantId, date, ct), bggId, startsAt);
    }

    // A read operation loads each community/day once. No cache survives mutations or requests.
    public async Task<IReadOnlyDictionary<Guid, GameProviderResponse>> ForGatheringsAsync(
        IEnumerable<GameGathering> gatherings, long participantId, CancellationToken ct)
    {
        var result = new Dictionary<Guid, GameProviderResponse>();
        foreach (var communityGroup in gatherings.GroupBy(x => x.CommunityKey))
        {
            var community = await db.OyinQCommunities.AsNoTracking().SingleAsync(x => x.Key == communityGroup.Key, ct);
            foreach (var day in communityGroup.GroupBy(x => community.Mode == BotMode.Camp
                ? CommunityTime.LocalDate(x.StartsAtUtc, community.TimeZoneId) : (DateOnly?)null))
            {
                var context = await LoadContext(community, participantId, day.Key, ct);
                foreach (var g in day)
                {
                    var state = Evaluate(context, GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson).BggId ?? 0, g.StartsAtUtc);
                    result[g.PublicId] = state with { CanBring = state.CanBring && GatheringLifecycle.IsUpcoming(g, clock.GetUtcNow()) };
                }
            }
        }
        return result;
    }

    private sealed record ProviderContext(IReadOnlyDictionary<long, GameCatalogService.EffectiveGame> Games,
        IReadOnlySet<long> Owned, bool CanOffer, Camp? Camp = null);

    private async Task<ProviderContext> LoadContext(OyinQCommunity community, long participantId, DateOnly? date, CancellationToken ct)
    {
        var key = community.Key;
        if (community.Mode == BotMode.Club)
        {
            var telegramId = await db.Participants.Where(x => x.Id == participantId).Select(x => x.TelegramUserId).SingleAsync(ct);
            var clubGames = await catalog.LoadClubAsync(key, telegramId, ct);
            return new(clubGames.ToDictionary(x => x.Game.BggId), clubGames.Where(x => x.IsOwned).Select(x => x.Game.BggId).ToHashSet(), false);
        }
        var games = await campCatalog.LoadAsync(key, participantId, ct, date);
        var camp = await db.Camps.AsNoTracking().Include(x => x.BotChat).SingleAsync(x => x.BotChatKey == key, ct);
        var registration = await db.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays)
            .SingleOrDefaultAsync(x => x.CampId == camp.Id && x.ParticipantId == participantId, ct);
        var eligible = camp.Status == CampStatus.Active && !CampParticipationPolicy.HasEnded(camp, community.TimeZoneId, clock.GetUtcNow())
            && CampParticipationPolicy.IsRegistrationComplete(registration, camp)
            && (date == null || registration!.SelectedDays.Any(x => x.Date == date));
        var owned = await db.ParticipantCollectionItems.Where(x => x.ParticipantId == participantId && x.ItemType == CollectionItemType.BaseGame)
            .Select(x => x.BggId).ToArrayAsync(ct);
        return new(games.ToDictionary(x => x.Game.BggId, x => new GameCatalogService.EffectiveGame(x.Game, x.IsInBaseCollection, x.Providers)),
            owned.ToHashSet(), eligible, camp);
    }

    private static GameProviderResponse Evaluate(ProviderContext context, long bggId, DateTimeOffset? startsAt)
    {
        var value = context.Games.GetValueOrDefault(bggId);
        var result = Describe(value?.IsInBaseCollection == true, value?.Providers ?? []);
        var withinWindow = startsAt is null || context.Camp is { } camp && CampOperatingWindow.Contains(camp, startsAt.Value);
        return result with { IsOwned = context.Owned.Contains(bggId), CanBring = context.CanOffer && withinWindow && context.Owned.Contains(bggId)
            && !result.Providers.Any(x => x.IsCurrentUser && x.Commitment == CampBringCommitment.Bringing) };
    }

    public async Task<GameProviderResponse> ForGatheringAsync(GameGathering g, long participantId, CancellationToken ct)
    {
        var value = await ForGameAsync(g.CommunityKey, GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson).BggId ?? 0,
            participantId, g.StartsAtUtc, ct);
        return value with { CanBring = value.CanBring && GatheringLifecycle.IsUpcoming(g, clock.GetUtcNow()) };
    }

    public async Task BringAsync(GameGathering g, long participantId, CancellationToken ct)
    {
        if (!GatheringLifecycle.IsUpcoming(g, clock.GetUtcNow())) throw new InvalidOperationException("Сбор уже недоступен для изменений.");
        var state = await ForGatheringAsync(g, participantId, ct);
        if (state.Providers.Any(x => x.IsCurrentUser && x.Commitment == CampBringCommitment.Bringing)) return;
        if (!state.CanBring) throw new UnauthorizedAccessException("Нужны собственная игра и регистрация на дату сбора.");
        var camp = await db.Camps.Include(x => x.BotChat).SingleAsync(x => x.BotChatKey == g.CommunityKey, ct);
        await participation.RequireCompleteRegistrationAsync(camp.Id, participantId, ct,
            CommunityTime.LocalDate(g.StartsAtUtc, camp.BotChat.TimeZoneId), g.StartsAtUtc);
        await contributions.SetCommitmentAsync(camp.Id, participantId,
            GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson).BggId ?? 0,
            CollectionItemType.BaseGame, CampBringCommitment.Bringing, ct, g.StartsAtUtc);
    }
}
