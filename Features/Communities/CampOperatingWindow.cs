using System.Globalization;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Communities;

public static class CommunityTime
{
    public static DateTimeOffset ParseLocal(string value, string timeZoneId)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local)) throw new ArgumentException("Укажите дату и время.");
        return ToUtc(local, timeZoneId);
    }
    public static DateTimeOffset ToUtc(DateTime local, string timeZoneId)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ArgumentException("Это местное время пропущено или повторяется при переводе часов. Выберите другое время.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
    public static DateOnly LocalDate(DateTimeOffset instant, string zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
}

public static class CampOperatingWindow
{
    public static IReadOnlyDictionary<string, string> AttendanceLabels(Camp camp)
    {
        if (camp.StartsAtUtc is not { } start || camp.EndsAtUtc is not { } end) return new Dictionary<string, string>();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(camp.BotChat.TimeZoneId);
        var localStart = TimeZoneInfo.ConvertTime(start, zone);
        var localEnd = TimeZoneInfo.ConvertTime(end, zone);
        var (first, last) = AttendanceDates(start, end, camp.BotChat.TimeZoneId);
        return Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(first.AddDays).ToDictionary(
            date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            date => string.Join(" · ", new[] {
                date == first && localStart.TimeOfDay != TimeSpan.Zero ? $"с {localStart:HH:mm}" : null,
                date == last && localEnd.TimeOfDay != TimeSpan.Zero ? $"до {localEnd:HH:mm}" : null
            }.Where(x => x != null)));
    }

    public static void Validate(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start) throw new ArgumentException("Окончание кэмпа должно быть позже начала.");
    }
    public static void RequireContains(Camp camp, DateTimeOffset instant)
    {
        if (camp.StartsAtUtc is not { } start || camp.EndsAtUtc is not { } end)
            throw new InvalidOperationException("Для кэмпа ещё не настроены начало и окончание.");
        if (!Contains(camp, instant))
            throw new InvalidOperationException("Время сбора должно входить в рабочий интервал кэмпа: начиная с открытия и до окончания.");
    }
    public static bool Contains(Camp camp, DateTimeOffset instant) =>
        camp.StartsAtUtc is { } start && camp.EndsAtUtc is { } end && instant >= start && instant < end;
    public static (DateOnly Start, DateOnly End) AttendanceDates(DateTimeOffset start, DateTimeOffset end, string zone)
    {
        Validate(start, end);
        return (CommunityTime.LocalDate(start, zone), CommunityTime.LocalDate(end.AddTicks(-1), zone));
    }
}
