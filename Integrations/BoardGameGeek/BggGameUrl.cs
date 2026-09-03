namespace oyinQ.Bot.Integrations.BoardGameGeek;

public static class BggGameUrl
{
    public static string? FromId(long? bggId) => bggId is > 0
        ? $"https://boardgamegeek.com/boardgame/{bggId.Value}"
        : null;
}
