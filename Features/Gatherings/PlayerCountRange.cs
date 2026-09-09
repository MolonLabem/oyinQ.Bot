namespace oyinQ.Bot.Features.Gatherings;

public readonly record struct PlayerCountRange(int Minimum, int Maximum, bool WasDefaulted)
{
    public const int DefaultMinimum = 1;
    public const int DefaultMaximum = 12;

    public static PlayerCountRange Normalize(int? minimum, int? maximum) =>
        minimum is >= 1 && maximum is >= 1 && minimum <= maximum
            ? new PlayerCountRange(minimum.Value, maximum.Value, false)
            : new PlayerCountRange(DefaultMinimum, DefaultMaximum, true);

    public PlayerCountRange WithExpansions(IEnumerable<oyinQ.Bot.Features.Collections.ClubCollectionExpansion> expansions)
    {
        var minimum = Minimum;
        var maximum = Maximum;
        foreach (var expansion in expansions)
        {
            var range = Normalize(expansion.MinPlayers, expansion.MaxPlayers);
            if (range.WasDefaulted) continue;
            minimum = Math.Min(minimum, range.Minimum);
            maximum = Math.Max(maximum, range.Maximum);
        }
        return new(minimum, maximum, WasDefaulted);
    }
}
