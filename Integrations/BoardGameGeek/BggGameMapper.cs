using oyinQ.Bot.Features.Collections;

namespace oyinQ.Bot.Integrations.BoardGameGeek;

public static class BggGameMapper
{
    public static ClubCollectionGame ToCollectionGame(ExternalGame game,
        IReadOnlyList<ClubCollectionExpansion>? expansions = null)
    {
        if (game.BggId is not > 0)
            throw new InvalidOperationException("Игра BGG должна иметь положительный ID.");

        return new ClubCollectionGame(game.BggId.Value, game.Name, game.ThumbnailImageUrl, game.ImageUrl,
            game.MinPlayers, game.MaxPlayers, game.BestPlayers, expansions ?? [], game.Types, game.Categories,
            game.Description, game.YearPublished, game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge,
            game.Type, game.Subdomains, game.CategoryItems, game.Mechanics, game.OriginalName);
    }

    public static CampContributionSnapshot ToContributionSnapshot(ExternalGame game,
        IReadOnlyList<long>? parentBggIds = null) => new(
        CampContributionSnapshot.CurrentVersion, game.Name, game.ThumbnailImageUrl, game.ImageUrl,
        game.MinPlayers, game.MaxPlayers, game.BestPlayers, game.Types, game.Categories, game.Description,
        game.YearPublished, game.MinPlayTimeMinutes, game.MaxPlayTimeMinutes, game.MinAge, game.Type,
        game.Subdomains, game.CategoryItems, game.Mechanics, parentBggIds, game.OriginalName);
}
