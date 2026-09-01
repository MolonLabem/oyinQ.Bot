using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using oyinQ.Bot.Common.Options;
using oyinQ.Bot.Data;
using oyinQ.Bot.Integrations.BoardGameGeek;

namespace oyinQ.Bot.Features.Collections;

public static class BggNameRefreshCommand
{
    public static async Task<int> RunAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration["Database:ConnectionString"]?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database__ConnectionString is required to refresh BGG names.");

        var bggOptions = BggOptions.FromConfiguration(configuration);
        if (!bggOptions.IsAvailable)
            throw new InvalidOperationException("BoardGameGeek__ApiToken is required to refresh BGG names.");

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var dbContext = new AppDbContext(dbOptions);
        var clubDocuments = await dbContext.Clubs.AsNoTracking()
            .Select(club => club.CollectionJson)
            .ToArrayAsync(cancellationToken);
        var contributionSnapshots = await dbContext.CampGameContributions.AsNoTracking()
            .Select(contribution => new { contribution.BggId, contribution.SnapshotJson })
            .ToArrayAsync(cancellationToken);

        var ids = clubDocuments.Select(ClubCollectionSerializer.Deserialize)
            .SelectMany(document => document.Games.Select(game => game.BggId)
                .Concat(document.Games.SelectMany(game => game.Expansions).Select(expansion => expansion.BggId)))
            .Concat(contributionSnapshots.Select(contribution => contribution.BggId))
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://boardgamegeek.com"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        var bggClient = new BoardGameGeekClient(httpClient, Options.Create(bggOptions));
        var items = await bggClient.GetItemsByIdsAsync(ids, cancellationToken);
        var names = items.Where(item => item.Game.BggId is > 0)
            .ToDictionary(item => item.Game.BggId!.Value, item => new BggResolvedName(
                item.Game.Name,
                string.IsNullOrWhiteSpace(item.Game.OriginalName) ? item.Game.Name : item.Game.OriginalName,
                string.Equals(item.Game.Name, item.Game.OriginalName, StringComparison.OrdinalIgnoreCase)
                    ? null : item.Game.Name));

        var changedClubs = 0;
        var changedContributions = 0;
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var club in await dbContext.Clubs.ToArrayAsync(cancellationToken))
        {
            var document = club.ReadCollection();
            var updated = document with
            {
                Games = document.Games.Select(game =>
                {
                    var baseName = names.GetValueOrDefault(game.BggId);
                    return game with
                    {
                        Name = baseName?.RussianName ?? game.Name,
                        OriginalName = baseName?.OriginalName ?? game.OriginalName,
                        Expansions = game.Expansions.Select(expansion =>
                        {
                            var expansionName = names.GetValueOrDefault(expansion.BggId);
                            return expansionName is null ? expansion : expansion with
                            {
                                Name = expansionName.RussianName ?? expansion.Name,
                                OriginalName = expansionName.OriginalName
                            };
                        }).ToArray()
                    };
                }).ToArray()
            };
            var updatedJson = ClubCollectionSerializer.Serialize(updated);
            if (string.Equals(club.CollectionJson, updatedJson, StringComparison.Ordinal)) continue;
            club.ReplaceCollection(updated, now);
            changedClubs++;
        }

        foreach (var contribution in await dbContext.CampGameContributions.ToArrayAsync(cancellationToken))
        {
            if (!names.TryGetValue(contribution.BggId, out var name)) continue;
            var snapshot = contribution.ReadSnapshot();
            var updatedJson = CampContributionSnapshotSerializer.Serialize(snapshot with
            {
                Name = name.RussianName ?? snapshot.Name,
                OriginalName = name.OriginalName
            });
            if (string.Equals(contribution.SnapshotJson, updatedJson, StringComparison.Ordinal)) continue;
            contribution.SnapshotJson = updatedJson;
            contribution.UpdatedAt = now;
            changedContributions++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var russianNames = names.Values.Count(name => name.RussianName is not null);
        Console.WriteLine($"BGG name refresh requested {ids.Length} unique IDs in {(ids.Length + 19) / 20} batches; "
            + $"resolved {names.Count}, including {russianNames} unambiguous Russian names.");
        Console.WriteLine($"Updated {changedClubs} Club collection documents and {changedContributions} Camp contributions; "
            + $"left {ids.Length - names.Count} unresolved IDs unchanged.");
        return 0;
    }
}
