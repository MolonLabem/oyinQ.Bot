using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Communities;

public static class CampRules
{
    public static int InclusiveDuration(DateOnly startDate, DateOnly endDate)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("Дата начала кэмпа не может быть позже даты окончания.");
        return endDate.DayNumber - startDate.DayNumber + 1;
    }

    public static void ValidateRegistrationDays(int daysStaying, DateOnly startDate, DateOnly endDate)
    {
        var duration = InclusiveDuration(startDate, endDate);
        if (daysStaying < 1 || daysStaying > duration)
            throw new ArgumentOutOfRangeException(nameof(daysStaying),
                $"Количество дней должно быть от 1 до {duration}.");
    }

    public static string NormalizeCity(string? value)
    {
        var city = value?.Trim();
        if (string.IsNullOrWhiteSpace(city) || city.Length > 100)
            throw new ArgumentException("Укажите город (не более 100 символов).", nameof(value));
        return city;
    }

    public static void ValidateTransition(CampStatus current, CampStatus next)
    {
        if (current == next) return;
        var allowed = current switch
        {
            CampStatus.Draft => next is CampStatus.Active or CampStatus.Cancelled,
            CampStatus.Active => next is CampStatus.Closed or CampStatus.Cancelled,
            CampStatus.Closed => next == CampStatus.Cancelled,
            CampStatus.Cancelled => false,
            _ => false
        };
        if (!allowed)
            throw new InvalidOperationException($"Переход кэмпа из статуса «{Label(current)}» в «{Label(next)}» недоступен.");
    }

    public static void EnsureBaseSnapshotMutable(CampStatus status)
    {
        if (status != CampStatus.Draft)
            throw new InvalidOperationException(
                "Базовую коллекцию можно менять только пока кэмп является черновиком.");
    }

    public static void EnsureCanClose(int futureActiveGatheringCount)
    {
        if (futureActiveGatheringCount > 0)
            throw new InvalidOperationException(
                $"В кэмпе есть {futureActiveGatheringCount} будущих сборов. Сначала отмените их.");
    }

    private static string Label(CampStatus status) => status switch
    {
        CampStatus.Draft => "черновик",
        CampStatus.Active => "активен",
        CampStatus.Closed => "закрыт",
        CampStatus.Cancelled => "отменён",
        _ => status.ToString()
    };
}
