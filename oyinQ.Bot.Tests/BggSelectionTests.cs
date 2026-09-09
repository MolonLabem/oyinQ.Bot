using Microsoft.EntityFrameworkCore;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Features.Catalog;
using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class BggSelectionTests
{
    private static readonly BggCollectionItem Expansion = new(new ExternalGame(20, "Дополнение", 2, 5, "5", null,
        Description: "Правила", MinAge: 14, OriginalName: "Expansion"), true, [10, 11]);
    private static readonly BggGameDetails Base = new(new ExternalGame(10, "База", 2, 4, "4", null), []);

    [Fact]
    public async Task AddingExpansionToExistingClubGame_PreservesPreviouslySelectedExpansions()
    {
        var existing = new ClubCollectionGame(10, "Сохранённая база", null, null, 2, 4, null,
            [new(99, "Уже в коллекции")]);
        var details = await new BggSelectionService(new SelectionBggClient([Base], [Expansion]))
            .LoadBaseSelectionAsync(10, [20], default);
        var merged = BggGameMapper.ToCollectionSelection(details, [20], existing);
        var updated = ClubCollectionEditor.AddOrReplace(new(ClubCollectionDocument.CurrentVersion, [existing]), merged);
        var restored = Assert.Single(ClubCollectionSerializer.Deserialize(ClubCollectionSerializer.Serialize(updated)).Games);
        Assert.Equal("Сохранённая база", restored.Name);
        Assert.Equal([99L, 20L], restored.Expansions.Select(item => item.BggId));
        Assert.Equal(5, GatheringGameSnapshot.FromClubGame(restored, [20]).MaxPlayers);
        Assert.Equal(2, BggGameMapper.ToCollectionSelection(details, [20], restored).Expansions.Count);
        Assert.Throws<InvalidOperationException>(() => BggGameMapper.ToCollectionSelection(details, [1234], existing));
    }

    [Fact]
    public async Task MultipleParents_RequireExplicitOfficialBase_AndPreserveTheExpansionSelection()
    {
        var client = new SelectionBggClient([Base, new(new ExternalGame(11, "Другая база", 2, 4, null, null), [])], [Expansion]);
        var service = new BggSelectionService(client);
        var preview = await service.PreviewAsync(20, BggSelectionPurpose.BaseGame, null, default);
        Assert.Equal([10L, 11L], preview!.BaseGames.Select(item => item.BggId));
        Assert.Equal(CollectionItemType.Expansion, preview.ItemType);
        Assert.Equal([20L], preview.SelectedExpansionIds);
        var resolved = await service.PreviewAsync(20, BggSelectionPurpose.BaseGame, 10, default);
        Assert.Equal(10, resolved!.Details.Game.BggId);
        Assert.Equal(5, Assert.Single(resolved.Details.Expansions).MaxPlayers);
        Assert.Equal([20L], resolved.SelectedExpansionIds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(20, BggSelectionPurpose.BaseGame, 999, default));
        var personal = await service.PreviewAsync(20, BggSelectionPurpose.Ownership, null, default);
        Assert.Equal(20, personal!.Details.Game.BggId);
        Assert.Empty(personal.BaseGames);
    }

    [Fact]
    public async Task ManualAndImportProjections_PreserveTheSameRichExpansionAndAllParents()
    {
        var service = new BggSelectionService(new SelectionBggClient([Base], [Expansion]));
        // The expansion's inbound parent is authoritative even if the base omitted its link.
        var details = await service.LoadBaseSelectionAsync(10, [20], default);
        var manual = BggGameMapper.ToOwnership(details, [20]).Single(item => item.ItemType == CollectionItemType.Expansion);
        var imported = BggGameMapper.ToDraftItem(Expansion);
        Assert.Equal(CollectionItemSnapshotSerializer.Serialize(imported.Snapshot), CollectionItemSnapshotSerializer.Serialize(manual.Snapshot));
        Assert.Equal([10L, 11L], manual.ParentBggIds);
        Assert.Equal(5, manual.Snapshot.MaxPlayers);
        Assert.Equal("Правила", manual.Snapshot.Description);
        Assert.Equal(14, manual.Snapshot.MinAge);
        var ownership = await service.LoadOwnershipAsync(10, [20], default);
        Assert.Equal(2, ownership.Count);
        Assert.Equal(CollectionItemSnapshotSerializer.Serialize(imported.Snapshot), CollectionItemSnapshotSerializer.Serialize(ownership.Single(item => item.ItemType == CollectionItemType.Expansion).Snapshot));
    }

    [Fact]
    public async Task ClubAndCampAndPersonalSnapshots_RetainTheFifthPlayerOffline()
    {
        var details = await new BggSelectionService(new SelectionBggClient([Base], [Expansion])).LoadBaseSelectionAsync(10, [20], default);
        var game = BggGameMapper.ToCollectionGame(details);
        var document = ClubCollectionSerializer.Deserialize(ClubCollectionSerializer.Serialize(new(ClubCollectionDocument.CurrentVersion, [game])));
        var camp = EffectiveCampCatalogService.Build(document, []);
        foreach (var saved in new[] { document.Games.Single(), camp.Single().Game })
        {
            var snapshot = GatheringGameSnapshot.FromClubGame(saved, [20]);
            Assert.Equal(5, snapshot.MaxPlayers);
            Assert.Equal(4, snapshot.WithExpansions([]).MaxPlayers);
        }
        var personal = CollectionItemSnapshotSerializer.Deserialize(CollectionItemSnapshotSerializer.Serialize(BggGameMapper.ToDraftItem(Expansion).Snapshot));
        Assert.Equal(5, personal.ToExpansion(20).MaxPlayers);
    }

    [Fact]
    public async Task LegacyGatheringEnrichment_PreservesIdentityAndHistory_AndRejectsForeignParentMetadata()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var legacy = new GatheringGameSnapshot(3, 10, "Сохранённое название", null, null, 2, 4, null,
            [new(20, "Сохранённый доп")], KnownExpansions: [new(20, "Сохранённый доп"), new(21, "Другой доп")]);
        var original = GatheringGameSnapshotSerializer.Serialize(legacy);
        var client = new SelectionBggClient([Base], [Expansion, new(new ExternalGame(21, "Чужой", 1, 9, null, null), true, [999])]);
        var enriched = await new GatheringGameSelectionService(db, client).EnrichExpansionMetadataAsync(legacy, default);
        Assert.Equal("Сохранённое название", enriched.Name);
        Assert.Equal("Сохранённый доп", Assert.Single(enriched.SelectedExpansions).Name);
        Assert.Equal(5, enriched.MaxPlayers);
        Assert.Equal(4, enriched.BaseMaxPlayers);
        Assert.Null(enriched.KnownExpansions!.Single(item => item.BggId == 21).MaxPlayers);
        Assert.Equal(original, GatheringGameSnapshotSerializer.Serialize(legacy));
    }
}

internal sealed class SelectionBggClient(IReadOnlyList<BggGameDetails>? bases = null,
    IReadOnlyList<BggCollectionItem>? items = null) : IBoardGameGeekClient
{
    public Task<BggGameDetails?> GetGameDetailsAsync(long bggId, CancellationToken ct) =>
        Task.FromResult(bases?.SingleOrDefault(item => item.Game.BggId == bggId));
    public Task<IReadOnlyList<BggCollectionItem>> GetItemsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BggCollectionItem>>((items ?? []).Concat((bases ?? []).Select(item => new BggCollectionItem(item.Game, false, [])))
            .Where(item => ids.Contains(item.Game.BggId ?? 0)).ToArray());
    public Task<IReadOnlyList<BggBaseGameSearchResult>> SearchAsync(string query, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(string username, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(string username, CancellationToken ct) => throw new NotSupportedException();
}
