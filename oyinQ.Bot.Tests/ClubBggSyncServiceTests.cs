using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Integrations;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Tests;

public sealed class ClubBggSyncServiceTests
{
    [Fact]
    public async Task Preview_BuildsDeterministicDocumentAndDiff_WithMultiParentExpansion()
    {
        var client = new FakeBggClient
        {
            BaseGames =
            [
                Game(2, "Zulu", minPlayers: 2),
                Game(1, "Alpha", imageUrl: "https://example/new.jpg")
            ],
            Expansions =
            [
                new BggOwnedExpansion(Game(20, "Shared expansion"), [2, 1]),
                new BggOwnedExpansion(Game(21, "Orphan"), [99])
            ]
        };
        var current = new ClubCollectionDocument(1,
        [
            new ClubCollectionGame(1, "Alpha", null, "https://example/old.jpg", 1, 4, "3", []),
            new ClubCollectionGame(3, "Removed", null, null, null, null, null, [])
        ]);

        var preview = await new ClubBggSyncService(client)
            .PreviewAsync(" RollMove ", current, default);

        Assert.Equal("RollMove", preview.Username);
        Assert.Equal([1L, 2L], preview.Document.Games.Select(value => value.BggId));
        Assert.All(preview.Document.Games, game =>
            Assert.Equal(20, Assert.Single(game.Expansions).BggId));
        Assert.Equal(2, Assert.Single(preview.Added).BggId);
        Assert.Equal(3, Assert.Single(preview.Removed).BggId);
        Assert.Equal(1, Assert.Single(preview.Changed).BggId);
        Assert.Equal(21, Assert.Single(preview.OrphanExpansions).BggId);
        Assert.False(preview.IsEmpty);
    }

    [Fact]
    public async Task Preview_EmptyOwnedCollection_ReportsEveryCurrentGameAsRemoved()
    {
        var current = new ClubCollectionDocument(1,
        [
            new ClubCollectionGame(1, "First", null, null, null, null, null, []),
            new ClubCollectionGame(2, "Second", null, null, null, null, null, [])
        ]);

        var preview = await new ClubBggSyncService(new FakeBggClient())
            .PreviewAsync("RollMove", current, default);

        Assert.True(preview.IsEmpty);
        Assert.Empty(preview.Document.Games);
        Assert.Equal([1L, 2L], preview.Removed.Select(value => value.BggId));
    }

    [Fact]
    public async Task Preview_BggFailure_DoesNotConvertFailureToEmptyCollection()
    {
        var client = new FakeBggClient { Failure = new HttpRequestException("BGG failed") };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new ClubBggSyncService(client).PreviewAsync(
                "RollMove",
                ClubCollectionDocument.Empty,
                default));
    }

    [Fact]
    public async Task Preview_ExpansionOrderOnly_DoesNotReportMetadataChange()
    {
        var client = new FakeBggClient
        {
            BaseGames = [Game(1, "Alpha")],
            Expansions =
            [
                new BggOwnedExpansion(Game(20, "First"), [1]),
                new BggOwnedExpansion(Game(21, "Second"), [1])
            ]
        };
        var current = new ClubCollectionDocument(1,
        [
            new ClubCollectionGame(1, "Alpha", null, null, null, 4, "3",
            [
                new ClubCollectionExpansion(21, "Second"),
                new ClubCollectionExpansion(20, "First")
            ])
        ]);

        var preview = await new ClubBggSyncService(client)
            .PreviewAsync("RollMove", current, default);

        Assert.Empty(preview.Changed);
    }

    private static ExternalGame Game(
        long bggId,
        string name,
        int? minPlayers = null,
        string? imageUrl = null) =>
        new(bggId, name, minPlayers, 4, "3", $"https://boardgamegeek.com/boardgame/{bggId}", null, imageUrl);

    private sealed class FakeBggClient : IBoardGameGeekClient
    {
        public IReadOnlyList<ExternalGame> BaseGames { get; init; } = [];
        public IReadOnlyList<BggOwnedExpansion> Expansions { get; init; } = [];
        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<ExternalGame>> GetOwnedBaseGamesAsync(
            string username,
            CancellationToken cancellationToken) =>
            Failure is null
                ? Task.FromResult(BaseGames)
                : Task.FromException<IReadOnlyList<ExternalGame>>(Failure);

        public Task<IReadOnlyList<BggOwnedExpansion>> GetOwnedExpansionsAsync(
            string username,
            CancellationToken cancellationToken) => Task.FromResult(Expansions);

        public Task<IReadOnlyList<ExternalGameSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExternalGame?> GetGameAsync(long bggId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BggGameDetails?> GetGameDetailsAsync(long bggId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalGame>> GetOwnedCollectionAsync(string username, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExternalCollectionStep> GetOwnedCollectionStepAsync(string username, int offset, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
