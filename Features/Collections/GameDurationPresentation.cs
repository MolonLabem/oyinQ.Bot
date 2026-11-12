namespace oyinQ.Bot.Features.Collections;

public static class GameDurationPresentation
{
    // Display provider facts only; scheduling's fallback estimate is not game metadata.
    public static string? Format(int? minimum, int? maximum)
    {
        minimum = minimum is > 0 ? minimum : null;
        maximum = maximum is > 0 ? maximum : null;
        if (minimum > maximum) return null;
        if (minimum == maximum) return minimum is null ? null : $"{minimum} мин";
        if (minimum is null) return $"до {maximum} мин";
        return maximum is null ? $"от {minimum} мин" : $"{minimum}–{maximum} мин";
    }
}
