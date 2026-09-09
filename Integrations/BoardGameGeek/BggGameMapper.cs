using oyinQ.Bot.Features.Collections;
using oyinQ.Bot.Features.Gatherings;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public static class BggGameMapper
{
    public static ClubCollectionGame ToCollectionGame(ExternalGame game,
        IReadOnlyList<ClubCollectionExpansion>? expansions = null)
    {
        game = NormalizeMetadata(game);
        if (game.BggId is not > 0)
            throw new InvalidOperationException("Игра BGG должна иметь положительный ID.");

        return new ClubCollectionGame(game.BggId.Value, game.Name, game.ThumbnailImageUrl, game.ImageUrl,
            game.MinPlayers, game.MaxPlayers, game.BestPlayers, expansions ?? [], game.Types, game.Categories,
            game.Description, game.YearPublished, game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge,
            game.Type, game.Subdomains, game.CategoryItems, game.Mechanics, game.OriginalName);
    }

    public static CollectionItemSnapshot ToCollectionSnapshot(ExternalGame game,
        IReadOnlyList<long>? parentBggIds = null)
    {
        game = NormalizeMetadata(game);
        var players = PlayerCountRange.Normalize(game.MinPlayers, game.MaxPlayers);
        return new(CollectionItemSnapshot.CurrentVersion, game.Name, game.ThumbnailImageUrl, game.ImageUrl,
            players.WasDefaulted ? null : players.Minimum, players.WasDefaulted ? null : players.Maximum,
            game.BestPlayers, game.Types, game.Categories, game.Description,
            game.YearPublished, game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge, game.Type,
            game.Subdomains, game.CategoryItems, game.Mechanics, parentBggIds, game.OriginalName);
    }

    private static ExternalGame NormalizeMetadata(ExternalGame game)
    {
        var minimum = game.MinPlayTimeMinutes is >= 0 ? game.MinPlayTimeMinutes : null;
        var maximum = game.MaxPlayTimeMinutes is >= 0 ? game.MaxPlayTimeMinutes : null;
        if (minimum > maximum) (minimum, maximum) = (maximum, minimum);
        var description = game.Description;
        if (description is { Length: > 20_000 })
        {
            var length = char.IsHighSurrogate(description[19_999]) ? 19_999 : 20_000;
            description = description[..length];
        }
        return game with
        {
            Description = description,
            YearPublished = game.YearPublished is >= 1000 and <= 3000 ? game.YearPublished : null,
            MinPlayTimeMinutes = minimum,
            MaxPlayTimeMinutes = maximum,
            MinAge = game.MinAge is >= 0 and <= 100 ? game.MinAge : null
        };
    }
}
