using System.Globalization;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Communities;

public static class CampRules
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

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
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt,
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);

    public static void EnsureGatheringDateWithinRange(Camp camp, DateOnly gatheringDate)
    {
        if (camp.StartDate is not { } startDate || camp.EndDate is not { } endDate)
            throw new InvalidOperationException("Для кэмпа ещё не настроены даты.");
        if (gatheringDate < startDate || gatheringDate > endDate)
            throw new InvalidOperationException(
                $"Дата сбора должна быть в пределах дат кэмпа: {FormatDateRange(startDate, endDate)}.");
    }

    private static string FormatDateRange(DateOnly startDate, DateOnly endDate)
    {
        if (startDate == endDate) return startDate.ToString("d MMMM", RussianCulture);
        return startDate.Month == endDate.Month && startDate.Year == endDate.Year
            ? $"{startDate.Day}–{endDate.ToString("d MMMM", RussianCulture)}"
            : $"{startDate.ToString("d MMMM", RussianCulture)} – {endDate.ToString("d MMMM", RussianCulture)}";
    }

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
