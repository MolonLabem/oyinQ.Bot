using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;
using static oyinQ.Bot.Features.Collections.CampContributionSelectionService;

namespace oyinQ.Bot.Features.Catalog;

public sealed record CampWishPerson(Guid Id, string Name, DateOnly[] Dates);
public sealed record CampWishOwner(CampWishPerson Person, string Status, DateOnly[] Dates, string RequestState, bool IsMe);
public sealed record CampWishGame(ClubCollectionGame Game, int InterestedParticipants, int OtherInterested,
    bool IsWished, bool IsOwned, bool Confirmed, string BoxSummary, int ScheduledGatherings, string? MyStatus);
public sealed record CampWishQuery(string Mode = "all", string? Search = null, bool NeedsBox = false,
    bool WithoutGathering = false, bool Owned = false, string Sort = "demand", int Page = 1, bool ShowDeclined = false);
public sealed record CampWishPage(IReadOnlyList<CampWishGame> Items, int Total, int Suggestions, bool HasMore,
    bool CanAct, bool ShareCollection, bool ShareWishes, DateOnly[] MyDates, DateTimeOffset UpdatedAt);
public sealed record CampWishDetails(CampWishGame Item, IReadOnlyList<CampWishPerson> Interested,
    int AnonymousCount, IReadOnlyList<CampWishOwner> Owners, bool CanAct, DateOnly[] MyDates, IReadOnlyList<CampOutgoing> Requests,
    IReadOnlyList<CampWishPerson> AskedMe);
public sealed record CampIncoming(long BggId, string Name, IReadOnlyList<CampWishPerson> Requesters, string State);
public sealed record CampOutgoing(long BggId, string Name, CampWishPerson Owner, DateOnly[] Dates, string State, bool CanCancel);
public sealed record CampProfileSettings(bool CanAct, bool ShareCollection, bool ShareWishes,
    DateOnly[] MyDates, long[] DeclinedGameIds, long[] SuggestedGameIds);

// Request-local projection only. Ownership and availability remain in their canonical stores.
public sealed class CampWishlistService(AppDbContext db, CampContributionSelectionService contributions, TimeProvider clock)
{
    private sealed record Context(Camp Camp, CampRegistration Me, CampRegistration[] Registrations,
        GameWish[] Wishes, ParticipantCollectionItem[] Owned, CampGameContribution[] Contributions,
        CampBringRequest[] Requests, Dictionary<long, int> Gatherings, ClubCollectionGame[] BaseGames,
        Dictionary<long, CampParticipantVisibility> Visibility)
    {
        public bool ShareCollection(long participantId) => Visibility.GetValueOrDefault(participantId)?.ShareCollection != false;
        public bool ShareWishes(long participantId) => Visibility.GetValueOrDefault(participantId)?.ShareWishes != false;
        public bool CanAct(DateTimeOffset now) => Camp.Status == CampStatus.Active && Camp.EndsAtUtc > now;
        public CampWishPerson Person(CampRegistration r) => new(r.Participant.PublicId,
            r.DisplayName ?? r.Participant.PreferredDisplayName ?? r.Participant.DisplayName,
            r.SelectedDays.Select(d => d.Date).Order().ToArray());
    }

    private async Task<Context> LoadAsync(string key, long actor, CancellationToken ct)
    {
        var camp = await db.Camps.AsNoTracking().Include(x => x.BotChat).SingleOrDefaultAsync(x => x.BotChatKey == key, ct)
            ?? throw new KeyNotFoundException("Кэмп не найден.");
        if (!camp.BotChat.IsActive || camp.BotChat.DeletedAt != null) throw new UnauthorizedAccessException("Кэмп недоступен.");
        var registrations = (await db.CampRegistrations.AsNoTracking().Include(x => x.SelectedDays).Include(x => x.Participant)
            .Where(x => x.CampId == camp.Id).ToArrayAsync(ct)).Where(x => CampParticipationPolicy.IsRegistrationComplete(x, camp)).ToArray();
        var me = registrations.SingleOrDefault(x => x.ParticipantId == actor)
            ?? throw new UnauthorizedAccessException("Сначала завершите регистрацию на этот кэмп.");
        var ids = registrations.Select(x => x.ParticipantId).ToArray();
        var visibility = await db.CampParticipantVisibilities.AsNoTracking().Where(x => x.CampId == camp.Id)
            .ToDictionaryAsync(x => x.ParticipantId, ct);
        var visibleIds = registrations.Where(x => visibility.GetValueOrDefault(x.ParticipantId)?.ShareCollection != false || x.ParticipantId == actor)
            .Select(x => x.ParticipantId).ToArray();
        var wishes = await db.GameWishes.AsNoTracking().Where(x => x.CommunityKey == key && ids.Contains(x.ParticipantId)).ToArrayAsync(ct);
        var owned = await db.ParticipantCollectionItems.AsNoTracking().Where(x => visibleIds.Contains(x.ParticipantId) && x.ItemType == CollectionItemType.BaseGame).ToArrayAsync(ct);
        var offered = await db.CampGameContributions.AsNoTracking().Where(x => x.CampId == camp.Id && ids.Contains(x.ParticipantId) && x.ItemType == CollectionItemType.BaseGame).ToArrayAsync(ct);
        offered = offered.Where(x => EffectiveDates(x, registrations.Single(r => r.ParticipantId == x.ParticipantId)).Length > 0).ToArray();
        // Requests are private to their owner and authors.
        var requests = await db.CampBringRequests.AsNoTracking().Include(x => x.Requesters).Include(x => x.Owner)
            .Where(x => x.CampId == camp.Id && (x.OwnerParticipantId == actor || x.Requesters.Any(r => r.ParticipantId == actor))).ToArrayAsync(ct);
        var snapshots = await GatheringListQuery.Apply(db.GameGatherings.AsNoTracking().Where(x => x.CommunityKey == key),
            GatheringListScope.Upcoming, clock.GetUtcNow()).Select(x => x.GameSnapshotJson).ToArrayAsync(ct);
        var gatherings = snapshots.Select(x => GatheringGameSnapshotSerializer.Deserialize(x).BggId).OfType<long>()
            .GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        var baseGames = (await new SharedCollectionReader(db).ForCampAsync(camp, ct)).Games.ToArray();
        return new(camp, me, registrations, wishes, owned, offered, requests, gatherings, baseGames, visibility);
    }

    private static CampWishGame Project(Context c, long id)
    {
        var wishes = c.Wishes.Where(x => x.BggId == id).ToArray();
        var owned = c.Owned.FirstOrDefault(x => x.BggId == id && x.ParticipantId == c.Me.ParticipantId);
        var offered = c.Contributions.Where(x => x.BggId == id).ToArray();
        var game = wishes.FirstOrDefault() is { } wish ? ClubCollectionSerializer.Deserialize(wish.SnapshotJson).Games.Single()
            : owned?.ReadSnapshot().ToCollectionGame(id) ?? offered.FirstOrDefault()?.ReadSnapshot().ToCollectionGame(id)
            ?? c.Owned.FirstOrDefault(x => x.BggId == id)?.ReadSnapshot().ToCollectionGame(id)
            ?? c.Requests.Where(x => x.BggId == id && x.SnapshotJson.Length > 0).Select(x => CollectionItemSnapshotSerializer.Deserialize(x.SnapshotJson).ToCollectionGame(id)).FirstOrDefault()
            ?? c.BaseGames.Single(x => x.BggId == id);
        var myDates = c.Me.SelectedDays.Select(x => x.Date).ToHashSet();
        var eligible = offered.Where(x => EffectiveDates(x, c.Registrations.Single(r => r.ParticipantId == x.ParticipantId)).Any(myDates.Contains)).ToArray();
        var confirmed = eligible.Where(x => x.Commitment == CampBringCommitment.Bringing).ToArray();
        var first = confirmed.FirstOrDefault() ?? eligible.FirstOrDefault();
        var visibleOwners = c.Owned.Any(x => x.BggId == id && x.ParticipantId != c.Me.ParticipantId);
        var summary = first is null ? visibleOwners ? "Есть у участников — можно попросить" : "Коробку пока не нашли"
            : $"{c.Person(c.Registrations.Single(r => r.ParticipantId == first.ParticipantId)).Name} "
              + (first.Commitment == CampBringCommitment.Bringing ? "точно привезёт" : "может привезти — пока без подтверждения")
              + " · " + string.Join(", ", EffectiveDates(first, c.Registrations.Single(r => r.ParticipantId == first.ParticipantId)).Select(d => d.ToString("dd.MM")));
        var mine = offered.SingleOrDefault(x => x.ParticipantId == c.Me.ParticipantId);
        var declined = c.Requests.Any(x => x.BggId == id && x.OwnerParticipantId == c.Me.ParticipantId && x.Declined);
        return new(game, wishes.Length, wishes.Count(x => x.ParticipantId != c.Me.ParticipantId
                && c.Registrations.Single(r => r.ParticipantId == x.ParticipantId).SelectedDays.Any(d => myDates.Contains(d.Date))),
            wishes.Any(x => x.ParticipantId == c.Me.ParticipantId), owned != null, confirmed.Length > 0, summary,
            c.Gatherings.GetValueOrDefault(id), declined ? "declined" : mine?.Commitment.ToString());
    }

    public async Task<CampProfileSettings> SettingsAsync(string key, long actor, CancellationToken ct)
    {
        var c = await LoadAsync(key, actor, ct);
        return new(c.CanAct(clock.GetUtcNow()), c.ShareCollection(actor), c.ShareWishes(actor), c.Person(c.Me).Dates,
            c.Requests.Where(x => x.OwnerParticipantId == actor && x.Declined).Select(x => x.BggId).ToArray(),
            c.Wishes.Select(x => x.BggId).Distinct().Select(id => Project(c, id))
                .Where(x => x.IsOwned && x.OtherInterested > 0 && x.MyStatus != "declined")
                .OrderBy(x => x.Confirmed).ThenByDescending(x => x.OtherInterested).Select(x => x.Game.BggId).ToArray());
    }

    public async Task<CampWishPage> ListAsync(string key, long actor, CampWishQuery query, CancellationToken ct)
    {
        var c = await LoadAsync(key, actor, ct);
        var all = c.Wishes.Select(x => x.BggId).Distinct().Select(id => Project(c, id)).ToArray();
        var suggestions = all.Count(x => x.IsOwned && x.OtherInterested > 0 && x.MyStatus != "declined");
        IEnumerable<CampWishGame> items = query.Mode switch {
            "mine" => all.Where(x => x.IsWished),
            "bring" => all.Where(x => x.IsOwned && x.OtherInterested > 0 && (query.ShowDeclined || x.MyStatus != "declined")),
            _ => all
        };
        items = Filter(items, query);
        var result = items.ToArray();
        var page = Math.Max(1, query.Page);
        return new(result.Skip((page - 1) * 20).Take(20).ToArray(), result.Length, suggestions, page * 20 < result.Length,
            c.CanAct(clock.GetUtcNow()), c.ShareCollection(actor), c.ShareWishes(actor), c.Person(c.Me).Dates, clock.GetUtcNow());
    }

    public static IEnumerable<CampWishGame> Filter(IEnumerable<CampWishGame> items, CampWishQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Search)) items = items.Where(x => x.Game.Name.Contains(q.Search.Trim(), StringComparison.OrdinalIgnoreCase)
            || x.Game.OriginalName?.Contains(q.Search.Trim(), StringComparison.OrdinalIgnoreCase) == true);
        if (q.NeedsBox) items = items.Where(x => !x.Confirmed);
        if (q.WithoutGathering) items = items.Where(x => x.ScheduledGatherings == 0);
        if (q.Owned) items = items.Where(x => x.IsOwned);
        return q.Sort == "name" ? items.OrderBy(x => x.Game.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Game.BggId)
            : items.OrderBy(x => q.Mode == "bring" && x.Confirmed).ThenByDescending(x => x.InterestedParticipants).ThenBy(x => x.Game.Name).ThenBy(x => x.Game.BggId);
    }

    public async Task<CampWishDetails> DetailsAsync(string key, long actor, long id, CancellationToken ct)
    {
        var c = await LoadAsync(key, actor, ct);
        if (!c.Wishes.Any(x => x.BggId == id) && !c.Owned.Any(x => x.BggId == id) && !c.Contributions.Any(x => x.BggId == id) && !c.BaseGames.Any(x => x.BggId == id) && !c.Requests.Any(x => x.BggId == id))
            throw new KeyNotFoundException("Игра недоступна в этом кэмпе.");
        var interestedIds = c.Wishes.Where(x => x.BggId == id).Select(x => x.ParticipantId).ToHashSet();
        var interested = c.Registrations.Where(x => interestedIds.Contains(x.ParticipantId) && (c.ShareWishes(x.ParticipantId) || x.ParticipantId == actor)).Select(c.Person).ToArray();
        var owners = c.Owned.Where(x => x.BggId == id).Select(x => x.ParticipantId)
            .Concat(c.Contributions.Where(x => x.BggId == id).Select(x => x.ParticipantId)).Distinct().Select(owner => {
                var registration = c.Registrations.Single(x => x.ParticipantId == owner);
                var contribution = c.Contributions.SingleOrDefault(x => x.BggId == id && x.ParticipantId == owner);
                var request = c.Requests.SingleOrDefault(x => x.OwnerParticipantId == owner && x.BggId == id);
                var member = request?.Requesters.SingleOrDefault(x => x.ParticipantId == actor && x.Active);
                var dates = contribution is null ? c.Person(registration).Dates : EffectiveDates(contribution, registration);
                var needed = member?.Dates ?? c.Person(c.Me).Dates.Intersect(dates).ToArray();
                var found = Covered(c, id, needed);
                var state = found ? "found" : request?.Declined == true ? "declined" : member != null ? "pending" : "none";
                return new CampWishOwner(c.Person(registration), contribution?.Commitment.ToString() ?? "owned", dates, state, actor == owner);
            }).ToArray();
        var askedMe = c.Requests.Where(x => x.OwnerParticipantId == actor && x.BggId == id).SelectMany(x => x.Requesters)
            .Where(x => x.Active && c.Registrations.Any(r => r.ParticipantId == x.ParticipantId))
            .Select(x => c.Person(c.Registrations.Single(r => r.ParticipantId == x.ParticipantId)) with { Dates = x.Dates }).ToArray();
        return new(Project(c, id), interested, interestedIds.Count - interested.Length, owners, c.CanAct(clock.GetUtcNow()), c.Person(c.Me).Dates, Outgoing(c).Where(x => x.BggId == id).ToArray(), askedMe);
    }

    private static CampOutgoing[] Outgoing(Context c) => c.Requests.Where(x => x.Requesters.Any(r => r.ParticipantId == c.Me.ParticipantId))
        .Select(r => {
            var member = r.Requesters.Single(x => x.ParticipantId == c.Me.ParticipantId);
            var owner = c.Registrations.SingleOrDefault(x => x.ParticipantId == r.OwnerParticipantId);
            var currentDates = member.Dates.Intersect(c.Me.SelectedDays.Select(x => x.Date)).ToArray();
            var state = !member.Active ? "cancelled" : Covered(c, r.BggId, currentDates) ? "found" : r.Declined ? "declined"
                : owner == null || !currentDates.Intersect(owner.SelectedDays.Select(x => x.Date)).Any() ? "unavailable" : "pending";
            return new CampOutgoing(r.BggId, CollectionItemSnapshotSerializer.Deserialize(r.SnapshotJson).Name,
                owner == null ? new(r.Owner.PublicId, "Участник больше не зарегистрирован", []) : c.Person(owner), member.Dates, state, member.Active);
        }).ToArray();

    public async Task<IReadOnlyList<CampOutgoing>> OutgoingAsync(string key, long actor, CancellationToken ct) => Outgoing(await LoadAsync(key, actor, ct));

    private static bool Covered(Context c, long id, DateOnly[] dates) => dates.Length > 0 && dates.All(date =>
        c.Contributions.Any(x => x.BggId == id && x.Commitment == CampBringCommitment.Bringing
            && EffectiveDates(x, c.Registrations.Single(r => r.ParticipantId == x.ParticipantId)).Contains(date)));

    public async Task<object> ProfileAsync(string key, long actor, Guid? person, string? search, string? tab, int page, CancellationToken ct)
    {
        var c = await LoadAsync(key, actor, ct);
        var target = person == null ? c.Me : c.Registrations.SingleOrDefault(x => x.Participant.PublicId == person)
            ?? throw new KeyNotFoundException("Участник не найден в этом кэмпе.");
        var self = actor == target.ParticipantId;
        var ids = tab == "wishes" ? c.Wishes.Where(x => x.ParticipantId == target.ParticipantId && (self || c.ShareWishes(target.ParticipantId))).Select(x => x.BggId)
            : c.Contributions.Where(x => x.ParticipantId == target.ParticipantId).Select(x => x.BggId)
                .Concat(c.Owned.Where(x => x.ParticipantId == target.ParticipantId).Select(x => x.BggId));
        var games = Filter(ids.Distinct().Select(id => {
            var item = Project(c, id);
            if (tab == "wishes") return item;
            var contribution = c.Contributions.SingleOrDefault(x => x.ParticipantId == target.ParticipantId && x.BggId == id);
            var summary = contribution == null ? "Есть в коллекции — можно попросить" : contribution.Commitment == CampBringCommitment.Bringing ? "Точно привезёт" : "Может привезти — пока без подтверждения";
            return item with { BoxSummary = summary + (contribution == null ? "" : " · " + string.Join(", ", EffectiveDates(contribution, target).Select(d => d.ToString("dd.MM")))) };
        }), new(Search: search, Sort: "name")).ToArray();
        return new { Person = c.Person(target), IsMe = self, WishesVisible = self || c.ShareWishes(target.ParticipantId),
            Items = games.Skip((Math.Max(1, page) - 1) * 20).Take(20), Total = games.Length, HasMore = Math.Max(1, page) * 20 < games.Length };
    }

    public async Task<object> ParticipantsAsync(string key, long actor, string? search, int page, CancellationToken ct)
    {
        var c = await LoadAsync(key, actor, ct);
        var people = c.Registrations.Select(c.Person).Where(x => string.IsNullOrWhiteSpace(search) || x.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name).ToArray();
        return new { Items = people.Skip((Math.Max(1, page) - 1) * 30).Take(30), Total = people.Length, HasMore = Math.Max(1, page) * 30 < people.Length };
    }

    public async Task<IReadOnlyList<CampIncoming>> IncomingAsync(string key, long actor, CancellationToken ct)
    {
        var c = await LoadAsync(key, actor, ct);
        return c.Requests.Where(x => x.OwnerParticipantId == actor).Select(r => {
            var members = r.Requesters.Where(x => x.Active && c.Registrations.Any(p => p.ParticipantId == x.ParticipantId)).ToArray();
            DateOnly[] Needed(CampBringRequester member) => member.Dates.Intersect(c.Registrations.Single(p => p.ParticipantId == member.ParticipantId).SelectedDays.Select(x => x.Date)).ToArray();
            var name = c.Owned.FirstOrDefault(x => x.BggId == r.BggId && x.ParticipantId == actor)?.ReadSnapshot().Name
                ?? c.Contributions.FirstOrDefault(x => x.BggId == r.BggId && x.ParticipantId == actor)?.ReadSnapshot().Name
                ?? CollectionItemSnapshotSerializer.Deserialize(r.SnapshotJson).Name;
            return new CampIncoming(r.BggId, name, members.Select(x => c.Person(c.Registrations.Single(p => p.ParticipantId == x.ParticipantId)) with { Dates = x.Dates }).ToArray(),
                members.Length > 0 && members.All(x => Covered(c, r.BggId, Needed(x))) ? "found" : r.Declined ? "declined"
                    : members.All(x => !Needed(x).Intersect(c.Person(c.Me).Dates).Any()) ? "unavailable" : "pending");
        }).Where(x => x.Requesters.Count > 0).ToArray();
    }

    public async Task ActAsync(string key, long actor, long id, string action, Guid? ownerId, DateOnly[]? dates,
        bool? shareCollection, bool? shareWishes, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var community = await CommunityMutationLock.AcquireAsync(db, key, ct);
        if (community.DeletedAt != null || !community.IsActive) throw new UnauthorizedAccessException("Кэмп недоступен.");
        var campId = await db.Camps.Where(x => x.BotChatKey == key).Select(x => x.Id).SingleAsync(ct);
        if (db.Database.IsRelational()) await db.Camps.FromSqlInterpolated($"SELECT * FROM \"Camps\" WHERE \"Id\" = {campId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
        // Serializes the persisted daily limit across Camps as well as ownership changes.
        if (db.Database.IsRelational()) await db.Participants.FromSqlInterpolated($"SELECT * FROM \"Participants\" WHERE \"Id\" = {actor} FOR UPDATE").AsNoTracking().SingleAsync(ct);
        var c = await LoadAsync(key, actor, ct);
        CampParticipationPolicy.EnsureAcceptsMutations(c.Camp, community.TimeZoneId, clock.GetUtcNow());
        if (action == "privacy")
        {
            var visibility = await db.CampParticipantVisibilities.SingleOrDefaultAsync(x => x.CampId == campId && x.ParticipantId == actor, ct);
            if (visibility == null)
            {
                visibility = new() { CampId = campId, ParticipantId = actor };
                db.CampParticipantVisibilities.Add(visibility);
            }
            if (shareCollection.HasValue) visibility.ShareCollection = shareCollection.Value;
            if (shareWishes.HasValue) visibility.ShareWishes = shareWishes.Value;
        }
        else if (action is "offer" or "confirm" or "decline" or "withdraw")
        {
            var own = c.Owned.SingleOrDefault(x => x.BggId == id && x.ParticipantId == actor);
            if (own == null && action is "offer" or "confirm") throw new InvalidOperationException("Сначала добавьте игру в свою коллекцию.");
            var decision = await db.CampBringRequests.SingleOrDefaultAsync(x => x.CampId == campId && x.OwnerParticipantId == actor && x.BggId == id, ct);
            if (action == "decline")
            {
                if (decision == null) { decision = new() { CampId = campId, OwnerParticipantId = actor, BggId = id,
                    SnapshotJson = own?.SnapshotJson ?? throw new KeyNotFoundException("Игра не найдена."), CreatedAt = clock.GetUtcNow() }; db.CampBringRequests.Add(decision); }
                decision.Declined = true;
            }
            else if (decision != null) decision.Declined = false;
            if (action is "decline" or "withdraw") await contributions.RemoveAsync(campId, actor, id, CollectionItemType.BaseGame, ct);
            else
            {
                var chosen = (dates ?? c.Person(c.Me).Dates).Distinct().Order().ToArray();
                if (chosen.Length == 0 || chosen.Except(c.Person(c.Me).Dates).Any()) throw new ArgumentException("Выберите дни из своей регистрации.");
                await contributions.SetCommitmentAsync(campId, actor, id, CollectionItemType.BaseGame,
                    action == "confirm" ? CampBringCommitment.Bringing : CampBringCommitment.Available, ct, availableDates: dates == null ? null : chosen);
            }
            await db.SaveChangesAsync(ct);
            if (action == "decline") await new CampWishlistNotifications(db, clock).DeclinedAsync(campId, actor, id, ct);
        }
        else if (action == "cancel")
        {
            var member = await db.CampBringRequesters.SingleOrDefaultAsync(x => x.Request.CampId == campId
                && x.Request.Owner.PublicId == ownerId && x.Request.BggId == id && x.ParticipantId == actor, ct);
            if (member != null) member.Active = false;
        }
        else if (action == "request")
        {
            var target = c.Registrations.SingleOrDefault(x => x.Participant.PublicId == ownerId && x.ParticipantId != actor)
                ?? throw new ArgumentException("Выберите другого участника этого кэмпа.");
            var request = await db.CampBringRequests.Include(x => x.Requesters).SingleOrDefaultAsync(x => x.CampId == campId && x.OwnerParticipantId == target.ParticipantId && x.BggId == id, ct);
            var member = request?.Requesters.SingleOrDefault(x => x.ParticipantId == actor);
            var visible = c.Owned.Any(x => x.ParticipantId == target.ParticipantId && x.BggId == id)
                || c.Contributions.Any(x => x.ParticipantId == target.ParticipantId && x.BggId == id);
            if (!visible) throw new UnauthorizedAccessException("Игра не опубликована владельцем для этого кэмпа.");
            if (request?.Declined == true) throw new InvalidOperationException("Владелец не сможет привезти эту игру на кэмп.");
            var common = c.Person(c.Me).Dates.Intersect(c.Person(target).Dates).ToArray();
            var chosen = (dates ?? common).Distinct().Order().ToArray();
            if (chosen.Length == 0 || chosen.Except(common).Any()) throw new ArgumentException("Выберите общие дни участия.");
            if (Covered(c, id, chosen)) throw new InvalidOperationException("Коробка уже найдена на выбранные дни.");
            if (member?.Active != true)
            {
                var since = clock.GetUtcNow().AddDays(-1);
                if (await db.CampBringRequesters.CountAsync(x => x.ParticipantId == actor && x.CreatedAt > since, ct) >= 10)
                    throw new InvalidOperationException("Можно отправить не больше 10 просьб за сутки. Попробуйте позже.");
            }
            if (request == null) { request = new() { CampId = campId, OwnerParticipantId = target.ParticipantId, BggId = id,
                SnapshotJson = c.Contributions.FirstOrDefault(x => x.ParticipantId == target.ParticipantId && x.BggId == id)?.SnapshotJson
                    ?? c.Owned.Single(x => x.ParticipantId == target.ParticipantId && x.BggId == id).SnapshotJson,
                CreatedAt = clock.GetUtcNow() }; db.CampBringRequests.Add(request); }
            if (member == null) { member = new() { ParticipantId = actor, CreatedAt = clock.GetUtcNow() }; request.Requesters.Add(member); }
            if (!member.Active) member.CreatedAt = clock.GetUtcNow();
            member.Active = true; member.Dates = chosen;
            await db.SaveChangesAsync(ct);
            await new CampWishlistNotifications(db, clock).RequestedAsync(campId, target.ParticipantId, id, ct);
        }
        else throw new ArgumentException("Неизвестное действие.");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
