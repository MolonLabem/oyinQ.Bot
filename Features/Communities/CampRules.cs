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

    public static void EnsureRegistrationDatesWithinRange(
        IEnumerable<DateOnly> selectedDates,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (selectedDates.Any(date => date < startDate || date > endDate))
            throw new InvalidOperationException("Новый диапазон не включает один или несколько подтверждённых дней участника.");
    }

    public static DateOnly GetLocalGatheringDate(DateTimeOffset startsAt, string timeZoneId) =>
        CommunityTime.LocalDate(startsAt, timeZoneId);

    public static IReadOnlyList<DateOnly> ValidateSelectedDates(IReadOnlyCollection<DateOnly> values,
        DateOnly startDate, DateOnly endDate)
    {
        if (values.Count == 0)
            throw new ArgumentException("Выберите хотя бы один день кэмпа.", nameof(values));
        var distinct = values.Distinct().Order().ToArray();
        if (distinct.Length != values.Count)
            throw new ArgumentException("Даты участия не должны повторяться.", nameof(values));
        if (distinct.Any(x => x < startDate || x > endDate))
            throw new ArgumentOutOfRangeException(nameof(values),
                "Все выбранные даты должны входить в диапазон кэмпа.");
        return distinct;
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
            CampStatus.Closed => false,
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
                "Общую коллекцию кэмпа можно менять только пока он в черновике.");
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
