namespace oyinQ.Bot.Features.Gatherings;

public readonly record struct PlayerCountRange(int Minimum, int Maximum, bool WasDefaulted)
{
    public const int DefaultMinimum = 1;
    public const int DefaultMaximum = 12;

    public static PlayerCountRange Normalize(int? minimum, int? maximum) =>
        minimum is >= 1 && maximum is >= 1 && minimum <= maximum
            ? new PlayerCountRange(minimum.Value, maximum.Value, false)
            : new PlayerCountRange(DefaultMinimum, DefaultMaximum, true);
}
