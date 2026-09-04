using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Communities;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Features.Notifications;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;
using oyinQ.Bot.Features.MiniApp;

namespace oyinQ.Bot.Tests;
public sealed class GatheringOwnershipCreationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalCreation_ExplicitOwnership_DeduplicatesAndNeverChangesClub(bool add)
    {
        await using var f = new PlanningFixture(); var seed = f.Gathering("club", f.Clock.Now.AddDays(1));
        await f.Db.SaveChangesAsync(); var club = await f.Db.Clubs.SingleAsync(); var original = club.CollectionJson;
        var bgg = new Bgg(); var service = CreateService(f, bgg);
        var command = new CreateGatheringCommand("club", "bgg", 42, [99], f.Clock.Now.AddHours(2), 1, 3, 4, null, true, true, add);
        var g = await service.CreateAsync(seed.Community.ToBotCommunity(), new(f.Me.TelegramUserId, null, "Первый"), command, default);
        await service.CreateAsync(seed.Community.ToBotCommunity(), new(f.Me.TelegramUserId, null, "Первый"), command with { StartsAt = f.Clock.Now.AddHours(4) }, default);
        Assert.Equal(add ? 2 : 0, await f.Db.ParticipantCollectionItems.CountAsync());
        Assert.Equal(2, bgg.Requests); Assert.Equal(original, club.CollectionJson);
        Assert.Equal(99, Assert.Single(GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson).SelectedExpansions).BggId);
        Assert.Empty(f.Db.CampGameContributions);
    }

    [Fact]
    public async Task CampCreation_ExplicitBringing_UsesOwnedItemsAndOnlyCurrentCamp()
    {
        await using var f = new PlanningFixture();
        var camp = new Camp { BotChat = new() { Key = "camp", Name = "Кэмп", Mode = BotMode.Camp, TimeZoneId = "UTC" },
            Status = CampStatus.Active, StartsAtUtc = f.Clock.Now.AddHours(-1), EndsAtUtc = f.Clock.Now.AddDays(2), BaseCollectionJson = ClubCollectionSerializer.Serialize(new(2, [])) };
        f.Db.Camps.Add(camp);
        f.Db.CampRegistrations.Add(new() { Camp = camp, Participant = f.Me, City = "Алматы", NeedsAccommodation = false,
            SelectedDays = [new() { Date = DateOnly.FromDateTime(f.Clock.Now.UtcDateTime) }] });
        await f.Db.SaveChangesAsync(); var bgg = new Bgg();
        await CreateService(f, bgg).CreateAsync(camp.BotChat.ToBotCommunity(), new(f.Me.TelegramUserId, null, "Первый"),
            new("camp", "bgg", 42, [99], f.Clock.Now.AddHours(1), 1, 3, 4, null, true, true, true, true), default);
        Assert.Equal(2, await f.Db.ParticipantCollectionItems.CountAsync());
        Assert.Equal(2, await f.Db.CampGameContributions.CountAsync());
        Assert.All(f.Db.CampGameContributions, x => { Assert.Equal(camp.Id, x.CampId); Assert.Equal(CampBringCommitment.Bringing, x.Commitment); });
        Assert.Equal(1, bgg.Requests);
    }

    [Fact]
    public async Task RejectedExpansion_DoesNotCreateOwnershipOrGathering()
    {
        await using var f = new PlanningFixture(); var seed = f.Gathering("club", f.Clock.Now.AddDays(1)); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(f, new Bgg()).CreateAsync(seed.Community.ToBotCommunity(),
            new(f.Me.TelegramUserId, null, "Первый"), new("club", "bgg", 42, [100], f.Clock.Now.AddHours(2), 1, 3, 4, null, true, true, true), default));
        Assert.Empty(f.Db.ParticipantCollectionItems); Assert.Single(f.Db.GameGatherings);
    }
    private static GatheringManagementService CreateService(PlanningFixture f, Bgg bgg) => new(f.Db, new(f.Db, bgg),
        new(f.Db, f.Clock), new(f.Db, new NotificationService(f.Db, f.Clock)), f.Clock);
    private sealed class Bgg : IBoardGameGeekClient
    {
        public int Requests { get; private set; }
        public Task<BggGameDetails?> GetGameDetailsAsync(long id, CancellationToken ct) { Requests++; return Task.FromResult<BggGameDetails?>(new(new ExternalGame(42, "Игра", 1, 4, null, "https://boardgamegeek.com/boardgame/42"), [new(99, "Дополнение")])); }
        public Task<IReadOnlyList<BggBaseGameSearchResult>> SearchAsync(string q, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => throw new NotSupportedException();
    }
}
