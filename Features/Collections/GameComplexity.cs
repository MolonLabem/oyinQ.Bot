namespace oyinQ.Bot.Features.Collections;

public enum GameComplexity
{
    Light = 1,
    MediumLight = 2,
    Medium = 3,
    MediumHeavy = 4,
    Heavy = 5
}

public interface IGameComplexity
{
    decimal? ComplexityWeight { get; }
    GameComplexity? Complexity { get; }
}

public sealed record ComplexityInfo(decimal? Weight, GameComplexity Level, string DisplayName, string CssClass);

public static class GameComplexityPresentation
{
    public static decimal? ValidWeight(decimal? weight) => weight is >= 1 and <= 5 ? weight : null;

    public static GameComplexity? Resolve(decimal? weight, GameComplexity? level)
    {
        if (level is >= GameComplexity.Light and <= GameComplexity.Heavy) return level;
        return ValidWeight(weight) is { } valid
            ? (GameComplexity)(int)decimal.Round(valid, 0, MidpointRounding.AwayFromZero) : null;
    }

    public static ComplexityInfo? Present(IGameComplexity game) => Present(game.ComplexityWeight, game.Complexity);

    public static ComplexityInfo? Present(decimal? weight, GameComplexity? level)
    {
        var resolved = Resolve(weight, level);
        if (resolved is null) return null;
        var (name, style) = resolved.Value switch
        {
            GameComplexity.Light => ("Очень лёгкая", "very-easy"),
            GameComplexity.MediumLight => ("Лёгкая", "easy"),
            GameComplexity.Medium => ("Средняя", "medium"),
            GameComplexity.MediumHeavy => ("Сложная", "hard"),
            _ => ("Очень сложная", "very-hard")
        };
        return new(ValidWeight(weight), resolved.Value, name, $"complexity-{style}");
    }
}
