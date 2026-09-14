using System.Globalization;
using System.Text;
using oyinQ.Bot.Integrations.Telegram;

namespace oyinQ.Bot.Features.Gatherings;

public static class GatheringCalendarExport
{
    public static byte[] Build(IEnumerable<ProfileGatheringPresentation> agenda, DateTimeOffset now, MiniAppLinkBuilder links)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//OyinQ//Personal agenda//RU", "CALSCALE:GREGORIAN" };
        foreach (var item in agenda)
        {
            lines.AddRange(["BEGIN:VEVENT", $"UID:{item.PublicId:N}@oyinq", $"DTSTAMP:{Date(now)}",
                $"DTSTART:{Date(item.StartsAtUtc)}", $"DTEND:{Date(item.EstimatedEndsAtUtc ?? item.StartsAtUtc.AddMinutes(GatheringPlayTiming.FallbackMinutes))}",
                $"SUMMARY:{Escape(item.GameName)}", $"LOCATION:{Escape(item.CommunityName)}",
                $"DESCRIPTION:{Escape(item.WaitlistPosition is { } position ? $"Лист ожидания: {position}. Место пока не подтверждено." : "Окончание оценочное. Актуальный состав и время — в OyinQ.")}",
                $"URL:{links.Gathering(item.CommunityKey, item.PublicId)}",
                item.WaitlistPosition is null ? "STATUS:CONFIRMED" : "STATUS:TENTATIVE", "END:VEVENT"]);
        }
        lines.Add("END:VCALENDAR");
        return Encoding.UTF8.GetBytes(string.Join("\r\n", lines.Select(Fold)) + "\r\n");
    }

    private static string Date(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    private static string Escape(string text) => new string(text.Where(x => !char.IsControl(x) || x is '\n' or '\r' or '\t').ToArray())
        .Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");

    // RFC 5545 section 3.1: fold by UTF-8 octets without splitting a code point.
    private static string Fold(string line)
    {
        var result = new StringBuilder(); var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 75) { result.Append("\r\n "); bytes = 1; }
            result.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}
